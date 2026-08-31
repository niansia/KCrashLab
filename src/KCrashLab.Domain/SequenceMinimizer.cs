using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed record MinimizationResult(
    CanonicalCase Original,
    CanonicalCase Minimized,
    int OracleAttempts,
    string StopReason)
{
    public double OperationReduction => Original.Value.Operations.Count == 0
        ? 0
        : 1d - ((double)Minimized.Value.Operations.Count / Original.Value.Operations.Count);

    public double ByteReduction => Original.CanonicalUtf8.Length == 0
        ? 0
        : 1d - ((double)Minimized.CanonicalUtf8.Length / Original.CanonicalUtf8.Length);
}

public static class SequenceMinimizer
{
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

        var current = original;
        var attempts = 0;
        var granularity = 2;

        while (current.Value.Operations.Count >= 2 && attempts < maximumAttempts)
        {
            var count = current.Value.Operations.Count;
            var chunkSize = (int)Math.Ceiling((double)count / granularity);
            var reduced = false;

            for (var start = 0; start < count && attempts < maximumAttempts; start += chunkSize)
            {
                var candidate = RemoveOperations(current, start, Math.Min(chunkSize, count - start));
                attempts++;
                var observed = await oracle(candidate, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(observed, targetSignature, StringComparison.Ordinal))
                {
                    continue;
                }

                current = candidate;
                granularity = Math.Max(2, granularity - 1);
                reduced = true;
                break;
            }

            if (reduced)
            {
                continue;
            }

            if (granularity >= count)
            {
                break;
            }

            granularity = Math.Min(count, granularity * 2);
        }

        var stopReason = attempts >= maximumAttempts ? "ATTEMPT_BUDGET" : "ONE_MINIMAL_FOR_SEQUENCE_DELETE";
        return new MinimizationResult(original, current, attempts, stopReason);
    }

    private static CanonicalCase RemoveOperations(CanonicalCase source, int start, int count)
    {
        var root = JsonNode.Parse(source.CanonicalUtf8)
            ?? throw new InvalidDataException("Canonical case cannot be parsed.");
        var operations = root["operations"]?.AsArray()
            ?? throw new InvalidDataException("Canonical case has no operations array.");
        for (var index = 0; index < count; index++)
        {
            operations.RemoveAt(start);
        }

        if (root["schedule"]?["delays_us"] is JsonArray delays)
        {
            for (var index = 0; index < count; index++)
            {
                delays.RemoveAt(start);
            }
        }

        return CaseCanonicalizer.Parse(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(root, ContractJson.Compact)));
    }
}
