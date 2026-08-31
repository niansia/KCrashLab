using System.Text.Json;
using KCrashLab.Contracts;
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

        if (Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories).Any(static path => path.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("A simulation bundle must not contain a memory dump.");
        }

        try
        {
            using var finding = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "finding.json"), cancellationToken).ConfigureAwait(false));
            using var environment = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "environment.json"), cancellationToken).ConfigureAwait(false));
            using var decision = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "decision.json"), cancellationToken).ConfigureAwait(false));
            using var discovery = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "runs", "discovery.run.json"), cancellationToken).ConfigureAwait(false));
            using var analysisDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "crash", "analysis.json"), cancellationToken).ConfigureAwait(false));

            RequireSimulation(finding.RootElement, "finding.json", errors);
            RequireSimulation(environment.RootElement, "environment.json", errors);
            RequireSimulation(decision.RootElement, "decision.json", errors);
            if (decision.RootElement.GetProperty("schema_version").GetInt32() != 2)
            {
                errors.Add("decision.json must use the provenance-bearing schema version 2.");
            }

            var provenance = ExperimentProvenanceBuilder.ParseAndValidateMinimizationReplay(
                decision.RootElement.GetProperty("provenance"));
            var originalBytes = await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "inputs", "original.case.json"), cancellationToken).ConfigureAwait(false);
            var original = CaseCanonicalizer.Parse(originalBytes);
            var minimizedBytes = await File.ReadAllBytesAsync(Path.Combine(bundleRoot, "inputs", "minimized.case.json"), cancellationToken).ConfigureAwait(false);
            var minimized = CaseCanonicalizer.Parse(minimizedBytes);
            var statedCaseId = finding.RootElement.GetProperty("case_id").GetString();
            if (!string.Equals(original.CaseId, statedCaseId, StringComparison.Ordinal))
            {
                errors.Add("finding.json case_id does not match canonical original case bytes.");
            }

            var analysis = analysisDocument.RootElement.Deserialize<KCrashLab.Contracts.TriageAnalysis>(KCrashLab.Contracts.ContractJson.Compact)
                ?? throw new InvalidDataException("analysis.json is empty.");
            var computedSignature = SignatureV1.Compute(analysis);
            var statedSignature = finding.RootElement.GetProperty("signature").GetString()
                ?? throw new InvalidDataException("finding.json signature is null.");
            if (!string.Equals(computedSignature, statedSignature, StringComparison.Ordinal))
            {
                errors.Add("finding.json signature does not match analysis.json.");
            }

            var replay = decision.RootElement.GetProperty("replay").Deserialize<ReplayDecision>(ContractJson.Compact)
                ?? throw new InvalidDataException("decision.json replay is empty.");
            var minimization = decision.RootElement.GetProperty("minimization");
            if (minimization.GetProperty("original_operations").GetInt32() != original.Value.Operations.Count
                || minimization.GetProperty("minimized_operations").GetInt32() != minimized.Value.Operations.Count
                || minimization.GetProperty("original_bytes").GetInt32() != originalBytes.Length
                || minimization.GetProperty("minimized_bytes").GetInt32() != minimizedBytes.Length
                || minimization.GetProperty("maximum_oracle_attempts").GetInt32() != provenance.MaximumOracleAttempts
                || minimization.GetProperty("oracle_attempts").GetInt32() > provenance.MaximumOracleAttempts)
            {
                errors.Add("decision.json minimization metrics do not match the verified cases or provenance controls.");
            }

            var expectedExperimentDigest = ExperimentProvenanceBuilder.M1ExperimentDefinitionDigest(
                provenance.Scenario,
                provenance.CampaignSeed,
                original.CaseId,
                statedSignature,
                provenance.MaximumOracleAttempts,
                replay.Policy,
                original.Value.SchemaVersion);
            var expectedMinimizerDigest = ExperimentProvenanceBuilder.M1MinimizerDefinitionDigest(
                original.CaseId,
                statedSignature,
                provenance.MaximumOracleAttempts,
                original.Value.SchemaVersion);
            var expectedReplayDigest = ExperimentProvenanceBuilder.M1ReplayPolicyDefinitionDigest(statedSignature, replay.Policy);
            if (!string.Equals(provenance.ExperimentDefinitionDigest, expectedExperimentDigest, StringComparison.Ordinal)
                || !string.Equals(provenance.MinimizerDefinitionDigest, expectedMinimizerDigest, StringComparison.Ordinal)
                || !string.Equals(provenance.ReplayPolicyDefinitionDigest, expectedReplayDigest, StringComparison.Ordinal))
            {
                errors.Add("M1 provenance definition digests do not match the verified evidence controls.");
            }

            var findingScenario = finding.RootElement.GetProperty("scenario").GetString();
            var discoveryScenario = discovery.RootElement.GetProperty("scenario").GetString();
            var discoveryCampaignId = discovery.RootElement.GetProperty("campaign_id").GetGuid();
            var expectedCampaignId = DeterministicIdentity.CreateGuid("campaign", provenance.Scenario, original.CaseId, provenance.CampaignSeed);
            if (!string.Equals(provenance.Scenario, findingScenario, StringComparison.Ordinal)
                || !string.Equals(provenance.Scenario, discoveryScenario, StringComparison.Ordinal)
                || discoveryCampaignId != expectedCampaignId)
            {
                errors.Add("M1 provenance scenario or campaign seed does not match the discovery record.");
            }

            foreach (var attempt in replay.Attempts)
            {
                var replayPath = $"runs/replay-{attempt.Attempt:D2}.run.json";
                if (!paths.Contains(replayPath))
                {
                    errors.Add($"Required replay evidence file is missing from the verified manifest: {replayPath}");
                }
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
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
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
