using System.Text.Json;
using KCrashLab.Domain;
using KCrashLab.Storage;

namespace KCrashLab.Controller;

public sealed record EvidenceVerificationReport(
    bool IsValid,
    int VerifiedFiles,
    IReadOnlyList<string> Errors);

public sealed class EvidenceBundleVerifier
{
    private static readonly string[] RequiredFiles =
    [
        "finding.json",
        "environment.json",
        "decision.json",
        "inputs/original.case.json",
        "inputs/minimized.case.json",
        "runs/discovery.run.json",
        "runs/campaign-events.json",
        "crash/windbg.raw.txt",
        "crash/analysis.json",
        "report/index.html"
    ];

    public static async Task<EvidenceVerificationReport> VerifyAsync(string bundleRoot, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        ManifestVerification manifest;
        try
        {
            manifest = await EvidenceManifest.VerifyAsync(bundleRoot, cancellationToken).ConfigureAwait(false);
            errors.AddRange(manifest.Errors);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new EvidenceVerificationReport(false, 0, [$"Manifest verification failed: {exception.Message}"]);
        }

        var paths = manifest.Verified.Select(static entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var required in RequiredFiles)
        {
            if (!paths.Contains(required))
            {
                errors.Add($"Required evidence file is missing from the verified manifest: {required}");
            }
        }

        if (Directory.EnumerateFiles(bundleRoot, "*.dmp", SearchOption.AllDirectories).Any())
        {
            errors.Add("A simulation bundle must not contain a memory dump.");
        }

        try
        {
            using var finding = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "finding.json"), cancellationToken).ConfigureAwait(false));
            using var environment = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "environment.json"), cancellationToken).ConfigureAwait(false));
            using var analysisDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "crash", "analysis.json"), cancellationToken).ConfigureAwait(false));

            RequireSimulation(finding.RootElement, "finding.json", errors);
            RequireSimulation(environment.RootElement, "environment.json", errors);
            var originalBytes = await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "inputs", "original.case.json"), cancellationToken).ConfigureAwait(false);
            var original = CaseCanonicalizer.Parse(originalBytes);
            var statedCaseId = finding.RootElement.GetProperty("case_id").GetString();
            if (!string.Equals(original.CaseId, statedCaseId, StringComparison.Ordinal))
            {
                errors.Add("finding.json case_id does not match canonical original case bytes.");
            }

            var analysis = analysisDocument.RootElement.Deserialize<KCrashLab.Contracts.TriageAnalysis>(KCrashLab.Contracts.ContractJson.Compact)
                ?? throw new InvalidDataException("analysis.json is empty.");
            var computedSignature = SignatureV1.Compute(analysis);
            var statedSignature = finding.RootElement.GetProperty("signature").GetString();
            if (!string.Equals(computedSignature, statedSignature, StringComparison.Ordinal))
            {
                errors.Add("finding.json signature does not match analysis.json.");
            }

            var claims = finding.RootElement.GetProperty("claims");
            if (claims.GetProperty("kernel_crash").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("root_cause").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("exploitability").GetString() != "NOT_ASSESSED")
            {
                errors.Add("Simulation claims policy is violated.");
            }

            var report = await File.ReadAllTextAsync(Path.Combine(bundleRoot, "report", "index.html"), cancellationToken).ConfigureAwait(false);
            if (!report.Contains("SIMULATED — NOT A REAL KERNEL CRASH", StringComparison.Ordinal))
            {
                errors.Add("Static report is missing the mandatory simulation banner.");
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or KeyNotFoundException)
        {
            errors.Add($"Semantic evidence verification failed: {exception.Message}");
        }

        return new EvidenceVerificationReport(errors.Count == 0, manifest.Verified.Count, errors);
    }

    private static void RequireSimulation(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty("execution_mode", out var mode) || mode.GetString() != "SIMULATED")
        {
            errors.Add($"{name} does not declare execution_mode SIMULATED.");
        }
    }
}
