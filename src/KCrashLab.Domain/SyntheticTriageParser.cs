using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static class SyntheticTriageParser
{
    public const int MaximumInputChars = 1_048_576;

    public static TriageAnalysis Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > MaximumInputChars)
        {
            throw new InvalidDataException("Triage fixture exceeds the parser size limit.");
        }

        string? fixtureVersion = null;
        string? executionMode = null;
        string? bugcheckCode = null;
        string? faultingModule = null;
        string? verifierRule = null;
        var parameters = new SortedDictionary<int, string>();
        var frames = new List<string>();
        var warnings = new List<string>();
        var inStack = false;

        foreach (var sourceLine in raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line == "STACK:")
            {
                inStack = true;
                continue;
            }

            if (line == "END_STACK")
            {
                inStack = false;
                continue;
            }

            if (inStack)
            {
                if (frames.Count < 64)
                {
                    frames.Add(SignatureV1.NormalizeFrame(line));
                }

                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "KCRASHLAB_FIXTURE_VERSION": fixtureVersion = value; break;
                case "EXECUTION_MODE": executionMode = value; break;
                case "BUGCHECK_CODE": bugcheckCode = value; break;
                case "FAULTING_MODULE": faultingModule = value; break;
                case "VERIFIER_RULE_ID": verifierRule = value; break;
                default:
                    if (key.StartsWith("BUGCHECK_P", StringComparison.Ordinal) && int.TryParse(key.AsSpan(10), out var index))
                    {
                        parameters[index] = value;
                    }

                    break;
            }
        }

        if (!string.Equals(executionMode, "SIMULATED", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Only explicitly SIMULATED triage fixtures are accepted in v1.");
        }

        if (fixtureVersion is null || bugcheckCode is null || faultingModule is null)
        {
            throw new InvalidDataException("Triage fixture is missing a required marker.");
        }

        if (frames.Count == 0)
        {
            warnings.Add("No stack frames were parsed.");
        }

        var confidence = frames.Count >= 5 ? ParseConfidence.High : frames.Count > 0 ? ParseConfidence.Medium : ParseConfidence.Low;
        return new TriageAnalysis(
            1,
            "SIMULATED",
            fixtureVersion,
            bugcheckCode,
            parameters.Values.ToArray(),
            faultingModule,
            verifierRule,
            frames,
            confidence,
            warnings);
    }
}

