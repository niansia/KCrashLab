using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed record MutationContext(
    long CampaignSeed,
    int MaximumCandidatesPerOperator = MutationCandidateSampling.DefaultMaximumCandidatesPerOperator);

public static class MutationCandidateSampling
{
    public const string AlgorithmId = "HASH_RANKED_V1";
    public const int DefaultMaximumCandidatesPerOperator = 64;

    public static IEnumerable<CanonicalCase> Select(
        IEnumerable<CanonicalCase> candidates,
        MutationContext context,
        string operatorId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(context.MaximumCandidatesPerOperator, 1);
        return candidates
            .Select(candidate => (Candidate: candidate, Rank: Rank(context.CampaignSeed, operatorId, candidate.CaseId)))
            .OrderBy(static item => item.Rank, StringComparer.Ordinal)
            .ThenBy(static item => item.Candidate.CaseId, StringComparer.Ordinal)
            .Take(context.MaximumCandidatesPerOperator)
            .Select(static item => item.Candidate);
    }

    private static string Rank(long seed, string operatorId, string caseId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{seed}\0{operatorId}\0{caseId}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

public interface ICaseMutationOperator
{
    string OperatorId { get; }

    IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context);
}

public static class DefaultMutationOperators
{
    public static IReadOnlyList<ICaseMutationOperator> Create() =>
    [
        new BoundaryScalarMutationOperator(),
        new PayloadBlockMutationOperator(),
        new SequenceDeleteMutationOperator(),
        new SequenceSwapMutationOperator(),
        new SequenceInsertMutationOperator()
    ];
}

public sealed class BoundaryScalarMutationOperator : ICaseMutationOperator
{
    private static readonly long[] InterestingValues = [-1, 0, 1, 2, 4, 8, 16, 64, 255, 256, int.MaxValue];

    public string OperatorId => "scalar.boundary.v1";

    public IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context)
    {
        return MutationCandidateSampling.Select(Enumerate(source, context), context, OperatorId);
    }

    private IEnumerable<CanonicalCase> Enumerate(CanonicalCase source, MutationContext context)
    {
        var sourceRoot = MutationJson.ParseRoot(source);
        var operations = MutationJson.GetOperations(sourceRoot);
        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            if (operations[operationIndex]?["fields"] is not JsonObject fields)
            {
                continue;
            }

            foreach (var field in fields.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                if (field.Value is not JsonValue value || !value.TryGetValue<long>(out var current))
                {
                    continue;
                }

                var values = InterestingValues
                    .Concat(CurrentNeighbors(current))
                    .Where(candidate => candidate != current)
                    .Distinct()
                    .Order()
                    .ToArray();
                foreach (var replacement in values)
                {
                    var root = sourceRoot.DeepClone().AsObject();
                    MutationJson.GetOperations(root)[operationIndex]!["fields"]![field.Key] = replacement;
                    var parameters = new JsonObject
                    {
                        ["operation_index"] = operationIndex,
                        ["field"] = field.Key,
                        ["from"] = current,
                        ["to"] = replacement,
                        ["campaign_seed"] = context.CampaignSeed
                    };
                    yield return MutationJson.Build(source, root, OperatorId, parameters);
                }
            }
        }
    }

    private static IEnumerable<long> CurrentNeighbors(long current)
    {
        if (current > long.MinValue)
        {
            yield return current - 1;
        }

        if (current < long.MaxValue)
        {
            yield return current + 1;
        }
    }
}

public sealed class PayloadBlockMutationOperator : ICaseMutationOperator
{
    public string OperatorId => "buffer.block.v1";

    public IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context)
    {
        return MutationCandidateSampling.Select(Enumerate(source, context), context, OperatorId);
    }

    private IEnumerable<CanonicalCase> Enumerate(CanonicalCase source, MutationContext context)
    {
        var sourceRoot = MutationJson.ParseRoot(source);
        var operations = MutationJson.GetOperations(sourceRoot);
        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            if (operations[operationIndex]?["fields"]?["payload"] is not JsonValue payloadNode
                || !payloadNode.TryGetValue<string>(out var payload))
            {
                continue;
            }

            var replacements = new[]
            {
                string.Empty,
                payload.Length == 0 ? "A" : payload[..1],
                payload[..(payload.Length / 2)],
                payload.Length <= 32_768 ? payload + payload : payload
            };
            foreach (var replacement in replacements.Distinct(StringComparer.Ordinal).Where(candidate => candidate != payload))
            {
                var root = sourceRoot.DeepClone().AsObject();
                MutationJson.GetOperations(root)[operationIndex]!["fields"]!["payload"] = replacement;
                var parameters = new JsonObject
                {
                    ["operation_index"] = operationIndex,
                    ["from_length"] = payload.Length,
                    ["to_length"] = replacement.Length,
                    ["campaign_seed"] = context.CampaignSeed
                };
                yield return MutationJson.Build(source, root, OperatorId, parameters);
            }
        }
    }
}

public sealed class SequenceDeleteMutationOperator : ICaseMutationOperator
{
    public string OperatorId => "sequence.delete.v1";

    public IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context)
    {
        return MutationCandidateSampling.Select(Enumerate(source, context), context, OperatorId);
    }

    private IEnumerable<CanonicalCase> Enumerate(CanonicalCase source, MutationContext context)
    {
        var sourceRoot = MutationJson.ParseRoot(source);
        var count = MutationJson.GetOperations(sourceRoot).Count;
        for (var index = 0; index < count; index++)
        {
            var root = sourceRoot.DeepClone().AsObject();
            MutationJson.GetOperations(root).RemoveAt(index);
            MutationJson.RemoveDelay(root, index);
            yield return MutationJson.Build(source, root, OperatorId, new JsonObject
            {
                ["operation_index"] = index,
                ["campaign_seed"] = context.CampaignSeed
            });
        }
    }
}

public sealed class SequenceSwapMutationOperator : ICaseMutationOperator
{
    public string OperatorId => "sequence.swap-adjacent.v1";

    public IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context)
    {
        return MutationCandidateSampling.Select(Enumerate(source, context), context, OperatorId);
    }

    private IEnumerable<CanonicalCase> Enumerate(CanonicalCase source, MutationContext context)
    {
        var sourceRoot = MutationJson.ParseRoot(source);
        var count = MutationJson.GetOperations(sourceRoot).Count;
        for (var index = 0; index + 1 < count; index++)
        {
            var root = sourceRoot.DeepClone().AsObject();
            var operations = MutationJson.GetOperations(root);
            (operations[index], operations[index + 1]) = (operations[index + 1]?.DeepClone(), operations[index]?.DeepClone());
            MutationJson.SwapDelays(root, index, index + 1);
            yield return MutationJson.Build(source, root, OperatorId, new JsonObject
            {
                ["left_index"] = index,
                ["right_index"] = index + 1,
                ["campaign_seed"] = context.CampaignSeed
            });
        }
    }
}

public sealed class SequenceInsertMutationOperator : ICaseMutationOperator
{
    private static readonly JsonObject[] Templates =
    [
        new JsonObject { ["ioctl"] = "RESET_STATE", ["input"] = string.Empty },
        new JsonObject { ["ioctl"] = "SET_MODE", ["fields"] = new JsonObject { ["mode"] = 0 } },
        new JsonObject { ["ioctl"] = "SUBMIT_RECORD", ["fields"] = new JsonObject { ["declared_len"] = 0, ["payload"] = string.Empty } },
        new JsonObject { ["ioctl"] = "QUERY_STATS" }
    ];

    public string OperatorId => "sequence.insert.v1";

    public IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context)
    {
        return MutationCandidateSampling.Select(Enumerate(source, context), context, OperatorId);
    }

    private IEnumerable<CanonicalCase> Enumerate(CanonicalCase source, MutationContext context)
    {
        var sourceRoot = MutationJson.ParseRoot(source);
        var count = MutationJson.GetOperations(sourceRoot).Count;
        for (var index = 0; index <= count; index++)
        {
            for (var templateIndex = 0; templateIndex < Templates.Length; templateIndex++)
            {
                if (count >= CaseCanonicalizer.MaximumOperations)
                {
                    yield break;
                }

                var root = sourceRoot.DeepClone().AsObject();
                MutationJson.GetOperations(root).Insert(index, Templates[templateIndex].DeepClone());
                MutationJson.InsertDelay(root, index, 0);
                yield return MutationJson.Build(source, root, OperatorId, new JsonObject
                {
                    ["operation_index"] = index,
                    ["template_index"] = templateIndex,
                    ["campaign_seed"] = context.CampaignSeed
                });
            }
        }
    }
}

internal static class MutationJson
{
    public static JsonObject ParseRoot(CanonicalCase source) =>
        JsonNode.Parse(source.CanonicalUtf8)?.AsObject()
        ?? throw new InvalidDataException("Canonical case is not a JSON object.");

    public static JsonArray GetOperations(JsonObject root) =>
        root["operations"]?.AsArray()
        ?? throw new InvalidDataException("Case has no operations array.");

    public static CanonicalCase Build(
        CanonicalCase source,
        JsonObject root,
        string operatorId,
        JsonObject parameters)
    {
        root["parent_case_id"] = source.CaseId;
        root["mutation"] = new JsonObject
        {
            ["operator_id"] = operatorId,
            ["parameters"] = parameters
        };
        return CaseCanonicalizer.Parse(Encoding.UTF8.GetString(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(root, ContractJson.Compact)));
    }

    public static void RemoveDelay(JsonObject root, int index)
    {
        if (root["schedule"]?["delays_us"] is JsonArray delays)
        {
            delays.RemoveAt(index);
        }
    }

    public static void InsertDelay(JsonObject root, int index, long value)
    {
        if (root["schedule"]?["delays_us"] is JsonArray delays)
        {
            delays.Insert(index, value);
        }
    }

    public static void SwapDelays(JsonObject root, int left, int right)
    {
        if (root["schedule"]?["delays_us"] is not JsonArray delays)
        {
            return;
        }

        (delays[left], delays[right]) = (delays[right]?.DeepClone(), delays[left]?.DeepClone());
    }
}
