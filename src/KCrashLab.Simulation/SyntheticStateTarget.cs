using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Simulation;

public static class SyntheticStateTarget
{
    private static readonly TriageAnalysis KnownAnalysis = new(
        1,
        "SIMULATED",
        "1",
        "0x000000c4",
        ["0x000000f6", "0x00000000", "0x00000000", "0x00000000"],
        "kclsynthetic",
        "0x000000f6",
        [
            "kclsynthetic!submitrecord",
            "kclsynthetic!dispatch",
            "kclsim!runoperation",
            "kclsim!runcase",
            "kclcontroller!execute"
        ],
        ParseConfidence.High,
        []);

    public static string KnownSignature { get; } = SignatureV1.Compute(KnownAnalysis);

    public static string? Evaluate(CanonicalCase testCase) => Observe(testCase).Signature;

    public static FuzzObservation Observe(CanonicalCase testCase)
    {
        var mode = -1;
        var resetObserved = false;
        var state = "uninitialized";
        var coverage = new HashSet<string>(StringComparer.Ordinal)
        {
            $"target:{testCase.Value.Target}",
            $"schedule:workers:{testCase.Value.Schedule?.Workers ?? 1}"
        };
        foreach (var operation in testCase.Value.Operations)
        {
            switch (operation.Ioctl)
            {
                case "RESET_STATE":
                    coverage.Add($"transition:{state}->reset");
                    mode = 0;
                    resetObserved = true;
                    state = "reset";
                    break;
                case "SET_MODE":
                    if (resetObserved)
                    {
                        mode = ReadInt32(operation.Fields, "mode") ?? 0;
                        coverage.Add($"transition:{state}->mode:{mode}");
                        state = $"mode:{mode}";
                    }
                    else
                    {
                        coverage.Add($"rejected:set-mode:{state}");
                    }

                    break;
                case "SUBMIT_RECORD" when resetObserved && mode == 2:
                    {
                        coverage.Add("path:submit:mode:2");
                        var declared = ReadInt32(operation.Fields, "declared_len") ?? 0;
                        var payload = ReadString(operation.Fields, "payload") ?? string.Empty;
                        if (declared > payload.Length)
                        {
                            coverage.Add("outcome:declared-length-exceeds-payload");
                            return new FuzzObservation(
                                "SYNTHETIC_FAILURE",
                                coverage.Order(StringComparer.Ordinal).ToArray(),
                                KnownSignature);
                        }

                        coverage.Add("outcome:record-accepted");
                        state = "record-accepted";
                        break;
                    }
                case "SUBMIT_RECORD":
                    coverage.Add($"path:submit:ignored:{state}");
                    break;
                case "QUERY_STATS":
                    coverage.Add($"path:query:{state}");
                    break;
                case "TRIGGER_ASYNC":
                    coverage.Add($"path:async:{state}");
                    break;
            }
        }

        coverage.Add($"outcome:complete:{state}");
        return new FuzzObservation("COMPLETE", coverage.Order(StringComparer.Ordinal).ToArray(), null);
    }

    private static int? ReadInt32(IReadOnlyDictionary<string, JsonElement>? fields, string name) =>
        fields is not null && fields.TryGetValue(name, out var element) && element.TryGetInt32(out var value) ? value : null;

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement>? fields, string name) =>
        fields is not null && fields.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
