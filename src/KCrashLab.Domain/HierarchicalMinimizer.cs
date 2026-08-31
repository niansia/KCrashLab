using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static class HierarchicalMinimizer
{
    private static readonly long[] InterestingIntegers = [0, 1, -1, 2, 4, 8, 16, 64, 255, 256, int.MaxValue];

    public static async Task<MinimizationResult> MinimizeAsync(
        CanonicalCase original,
        string targetSignature,
        Func<CanonicalCase, CancellationToken, Task<string?>> oracle,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSignature);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        var sequence = await SequenceMinimizer.MinimizeAsync(
            original,
            targetSignature,
            oracle,
            maximumAttempts,
            cancellationToken).ConfigureAwait(false);
        var current = sequence.Minimized;
        var attempts = sequence.OracleAttempts;
        var seen = new HashSet<string>(StringComparer.Ordinal) { current.CaseId };
        var changed = true;

        while (changed && attempts < maximumAttempts)
        {
            changed = false;
            if (ReadRoot(current)["schedule"] is not null)
            {
                var candidate = RemoveSchedule(current);
                if (seen.Add(candidate.CaseId))
                {
                    attempts++;
                    if (await MatchesAsync(candidate, targetSignature, oracle, cancellationToken).ConfigureAwait(false))
                    {
                        current = candidate;
                        changed = true;
                    }
                }
            }

            for (var operationIndex = 0;
                 operationIndex < current.Value.Operations.Count && attempts < maximumAttempts;
                 operationIndex++)
            {
                if (ReadOperation(current, operationIndex)["input"] is not null)
                {
                    var candidate = TransformOperation(current, operationIndex, static operation => operation.Remove("input"));
                    if (seen.Add(candidate.CaseId))
                    {
                        attempts++;
                        if (await MatchesAsync(candidate, targetSignature, oracle, cancellationToken).ConfigureAwait(false))
                        {
                            current = candidate;
                            changed = true;
                        }
                    }
                }

                var fields = ReadOperation(current, operationIndex)["fields"] as JsonObject;
                var fieldNames = fields?.Select(static field => field.Key).Order(StringComparer.Ordinal).ToArray() ?? [];
                foreach (var fieldName in fieldNames)
                {
                    if (attempts >= maximumAttempts)
                    {
                        break;
                    }

                    var removeCandidate = TransformOperation(current, operationIndex, operation =>
                    {
                        operation["fields"]?.AsObject().Remove(fieldName);
                    });
                    if (seen.Add(removeCandidate.CaseId))
                    {
                        attempts++;
                        if (await MatchesAsync(removeCandidate, targetSignature, oracle, cancellationToken).ConfigureAwait(false))
                        {
                            current = removeCandidate;
                            changed = true;
                            continue;
                        }
                    }

                    var fieldValue = ReadOperation(current, operationIndex)["fields"]?[fieldName];
                    foreach (var replacement in ShrinkValues(fieldValue))
                    {
                        if (attempts >= maximumAttempts)
                        {
                            break;
                        }

                        var candidate = TransformOperation(current, operationIndex, operation =>
                        {
                            operation["fields"]![fieldName] = replacement?.DeepClone();
                        });
                        if (!seen.Add(candidate.CaseId))
                        {
                            continue;
                        }

                        attempts++;
                        if (await MatchesAsync(candidate, targetSignature, oracle, cancellationToken).ConfigureAwait(false))
                        {
                            current = candidate;
                            changed = true;
                            break;
                        }
                    }
                }

                if (ReadOperation(current, operationIndex)["fields"] is JsonObject { Count: 0 })
                {
                    var candidate = TransformOperation(current, operationIndex, static operation => operation.Remove("fields"));
                    if (seen.Add(candidate.CaseId))
                    {
                        attempts++;
                        if (await MatchesAsync(candidate, targetSignature, oracle, cancellationToken).ConfigureAwait(false))
                        {
                            current = candidate;
                            changed = true;
                        }
                    }
                }
            }
        }

        var stopReason = attempts >= maximumAttempts
            ? "ATTEMPT_BUDGET"
            : "LOCAL_MINIMUM_HIERARCHICAL_V1";
        return new MinimizationResult(original, current, attempts, stopReason);
    }

    private static async Task<bool> MatchesAsync(
        CanonicalCase candidate,
        string targetSignature,
        Func<CanonicalCase, CancellationToken, Task<string?>> oracle,
        CancellationToken cancellationToken) =>
        string.Equals(
            await oracle(candidate, cancellationToken).ConfigureAwait(false),
            targetSignature,
            StringComparison.Ordinal);

    private static IEnumerable<JsonNode?> ShrinkValues(JsonNode? value)
    {
        if (value is not JsonValue jsonValue)
        {
            yield break;
        }

        if (jsonValue.TryGetValue<string>(out var text))
        {
            foreach (var replacement in new[]
                     {
                         string.Empty,
                         text.Length == 0 ? text : text[..1],
                         text[..(text.Length / 2)]
                     }.Distinct(StringComparer.Ordinal).Where(candidate => candidate != text).OrderBy(static candidate => candidate.Length))
            {
                yield return JsonValue.Create(replacement);
            }

            yield break;
        }

        if (jsonValue.TryGetValue<long>(out var integer))
        {
            foreach (var replacement in InterestingIntegers.Where(candidate => IsSimplerInteger(candidate, integer)))
            {
                yield return JsonValue.Create(replacement);
            }
        }
    }

    private static bool IsSimplerInteger(long candidate, long current)
    {
        var candidateText = candidate.ToString(CultureInfo.InvariantCulture);
        var currentText = current.ToString(CultureInfo.InvariantCulture);
        if (candidateText.Length != currentText.Length)
        {
            return candidateText.Length < currentText.Length;
        }

        return Magnitude(candidate) < Magnitude(current);
    }

    private static decimal Magnitude(long value) => Math.Abs((decimal)value);

    private static CanonicalCase RemoveSchedule(CanonicalCase source)
    {
        var root = ReadRoot(source);
        root.Remove("schedule");
        return Canonicalize(root);
    }

    private static CanonicalCase TransformOperation(CanonicalCase source, int operationIndex, Action<JsonObject> transform)
    {
        var root = ReadRoot(source);
        var operation = root["operations"]?[operationIndex]?.AsObject()
            ?? throw new InvalidDataException("Canonical case operation is missing.");
        transform(operation);
        return Canonicalize(root);
    }

    private static JsonObject ReadRoot(CanonicalCase source) =>
        JsonNode.Parse(source.CanonicalUtf8)?.AsObject()
        ?? throw new InvalidDataException("Canonical case is not a JSON object.");

    private static JsonObject ReadOperation(CanonicalCase source, int operationIndex) =>
        ReadRoot(source)["operations"]?[operationIndex]?.AsObject()
        ?? throw new InvalidDataException("Canonical case operation is missing.");

    private static CanonicalCase Canonicalize(JsonObject root) =>
        CaseCanonicalizer.Parse(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(root, ContractJson.Compact)));
}
