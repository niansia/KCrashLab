using System.Text.RegularExpressions;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static partial class RealWindbgParser
{
    public const int MaximumInputChars = 8 * 1_048_576;

    public static TriageAnalysis Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length > MaximumInputChars) throw new InvalidDataException("WinDbg output exceeds the 8 MiB parser limit.");

        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bugcheck = BugCheckRegex().Match(normalized);
        if (!bugcheck.Success) throw new InvalidDataException("WinDbg output does not contain a parseable BugCheck line.");

        var parameters = bugcheck.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeHex).Take(4).ToArray();
        var module = FirstValue(normalized, "MODULE_NAME")
                     ?? FirstValue(normalized, "IMAGE_NAME")
                     ?? "unknown";
        var verifierRule = FirstValue(normalized, "RULE_ID");
        var frames = ParseFrames(normalized);
        var warnings = new List<string>();
        if (module == "unknown") warnings.Add("WinDbg output did not identify a faulting module.");
        if (frames.Count == 0) warnings.Add("WinDbg output did not contain normalized stack frames.");
        var confidence = module != "unknown" && frames.Count >= 3 ? ParseConfidence.High
            : frames.Count > 0 ? ParseConfidence.Medium : ParseConfidence.Low;

        return new TriageAnalysis(
            1,
            "REAL_LAB",
            "real-windbg-v1",
            NormalizeHex(bugcheck.Groups[1].Value),
            parameters,
            module.Trim().ToLowerInvariant(),
            verifierRule,
            frames,
            confidence,
            warnings);
    }

    private static List<string> ParseFrames(string raw)
    {
        var frames = new List<string>();
        var inStack = false;
        foreach (var source in raw.Split('\n'))
        {
            var line = source.Trim();
            if (line.StartsWith("STACK_TEXT:", StringComparison.Ordinal)) { inStack = true; continue; }
            if (!inStack) continue;
            if (line.Length == 0 || line.EndsWith(':'))
            {
                if (frames.Count > 0) break;
                continue;
            }

            var match = StackFrameRegex().Match(line);
            if (match.Success && frames.Count < 64) frames.Add(SignatureV1.NormalizeFrame(match.Groups[1].Value));
        }
        return frames;
    }

    private static string? FirstValue(string raw, string key)
    {
        var match = Regex.Match(raw, $@"(?im)^\s*{Regex.Escape(key)}\s*:\s*(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string NormalizeHex(string value)
    {
        var token = value.Trim().Replace("`", string.Empty, StringComparison.Ordinal);
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) token = token[2..];
        token = token.TrimStart('0');
        if (token.Length == 0) token = "0";
        return token.PadLeft(token.Length > 8 ? 16 : 8, '0').ToLowerInvariant();
    }

    [GeneratedRegex(@"(?im)^\s*BugCheck\s+([0-9a-fA-Fx]+)\s*,\s*\{([^}]*)\}")]
    private static partial Regex BugCheckRegex();

    [GeneratedRegex(@"(?:^|\s)([A-Za-z0-9_.-]+!\S+)(?:\s|$)")]
    private static partial Regex StackFrameRegex();
}
