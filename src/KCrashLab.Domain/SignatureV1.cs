using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static partial class SignatureV1
{
    public static string Compute(TriageAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var parts = new List<string>
        {
            NormalizeToken(analysis.BugcheckCode)
        };
        parts.AddRange(analysis.RelevantParameters.Select(NormalizeToken));
        parts.Add(NormalizeToken(analysis.FaultingModule));
        parts.AddRange(analysis.NormalizedFrames.Take(5).Select(NormalizeFrame));
        parts.Add(NormalizeToken(analysis.VerifierRuleId ?? "none"));

        var bytes = Encoding.UTF8.GetBytes(string.Join("\0", parts));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string NormalizeFrame(string frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frame);
        var normalized = AddressPrefixRegex().Replace(frame.Trim(), string.Empty);
        normalized = OffsetSuffixRegex().Replace(normalized, string.Empty);
        normalized = WhitespaceRegex().Replace(normalized, " ");
        return normalized.ToLowerInvariant();
    }

    private static string NormalizeToken(string token) => token.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^(?:0x)?[0-9a-fA-F]{8,16}\s+")]
    private static partial Regex AddressPrefixRegex();

    [GeneratedRegex(@"\+0x[0-9a-fA-F]+(?:\s|$)")]
    private static partial Regex OffsetSuffixRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
