using System.Net;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Domain;
using KCrashLab.Storage;

namespace KCrashLab.Controller;

public sealed record EvidenceBuildResult(
    string BundleRoot,
    string ManifestPath,
    string FindingId,
    string Signature,
    int ManifestEntries);

public sealed class EvidenceBundleBuilder
{
    private static readonly string[] SimulationLimitations =
    [
        "This is a simulator result, not a real kernel crash.",
        "No memory dump, symbols, driver binary, or exploitability assessment exists."
    ];

    public static async Task<EvidenceBuildResult> BuildAsync(
        string bundleRoot,
        CampaignRunResult campaign,
        CanonicalCase original,
        MinimizationResult minimization,
        ReplayDecision replay,
        MinimizationReplayProvenance provenance,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ExperimentProvenanceBuilder.ValidateMinimizationReplay(provenance);
        if (campaign.State != CampaignState.Complete || campaign.Artifact is null || campaign.Analysis is null || campaign.Signature is null)
        {
            throw new InvalidOperationException("Only completed simulated findings can produce an evidence bundle.");
        }

        var expectedCampaignId = DeterministicIdentity.CreateGuid("campaign", provenance.Scenario, original.CaseId, provenance.CampaignSeed);
        var expectedExperimentDigest = ExperimentProvenanceBuilder.M1ExperimentDefinitionDigest(
            provenance.Scenario,
            provenance.ScenarioFixtureSchemaVersion,
            provenance.ScenarioFixtureDigestAlgorithm,
            provenance.ScenarioFixtureDigest,
            provenance.CampaignSeed,
            original.CaseId,
            campaign.Signature,
            provenance.MaximumOracleAttempts,
            replay.Policy,
            original.Value.SchemaVersion);
        var expectedMinimizerDigest = ExperimentProvenanceBuilder.M1MinimizerDefinitionDigest(
            original.CaseId,
            campaign.Signature,
            provenance.MaximumOracleAttempts,
            original.Value.SchemaVersion);
        var expectedReplayDigest = ExperimentProvenanceBuilder.M1ReplayPolicyDefinitionDigest(campaign.Signature, replay.Policy);
        if (campaign.CampaignId != expectedCampaignId
            || !string.Equals(campaign.Scenario, provenance.Scenario, StringComparison.Ordinal)
            || !MatchesSimulationEnvironment(campaign.CapabilityReport, provenance)
            || !string.Equals(provenance.ExperimentDefinitionDigest, expectedExperimentDigest, StringComparison.Ordinal)
            || !string.Equals(provenance.MinimizerDefinitionDigest, expectedMinimizerDigest, StringComparison.Ordinal)
            || !string.Equals(provenance.ReplayPolicyDefinitionDigest, expectedReplayDigest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("M1 provenance does not match the campaign, minimizer, and replay controls.");
        }

        var root = Path.GetFullPath(bundleRoot);
        Directory.CreateDirectory(root);
        var findingId = DeterministicIdentity.CreateGuid("finding", campaign.CampaignId, campaign.Signature);
        var runId = DeterministicIdentity.CreateGuid("run", campaign.CampaignId, original.CaseId);

        await WriteJsonAsync(Path.Combine(root, "finding.json"), new
        {
            schema_version = 1,
            execution_mode = "SIMULATED",
            finding_id = findingId,
            signature_version = 1,
            signature = campaign.Signature,
            case_id = original.CaseId,
            scenario = ReadScenario(campaign),
            classification = replay.Passed ? "SYNTHETIC_CONFIRMED" : "SYNTHETIC_FLAKY",
            claims = new
            {
                kernel_crash = "NOT_CLAIMED",
                root_cause = "NOT_CLAIMED",
                exploitability = "NOT_ASSESSED"
            },
            evidence_coverage = new Dictionary<string, string>
            {
                ["synthetic_artifact"] = "VERIFIED_BY_HASH",
                ["triage_input"] = "SYNTHETIC_FIXTURE",
                ["input_lineage"] = "VERIFIED",
                ["simulated_clean_replay"] = replay.Passed ? "VERIFIED" : "UNKNOWN",
                ["crash_dump"] = "NOT_APPLICABLE",
                ["symbols"] = "NOT_APPLICABLE",
                ["root_cause"] = "NOT_CLAIMED",
                ["exploitability"] = "NOT_ASSESSED"
            }
        }, cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(Path.Combine(root, "environment.json"), new
        {
            schema_version = 2,
            execution_mode = "SIMULATED",
            backend = SimulationEvidenceContract.BackendId,
            simulator_version = SimulationEvidenceContract.SimulatorVersion,
            virtual_epoch_utc = SimulationEvidenceContract.VirtualEpochUtc,
            scenario_fixture = new
            {
                schema_version = provenance.ScenarioFixtureSchemaVersion,
                name = provenance.Scenario,
                digest_algorithm = provenance.ScenarioFixtureDigestAlgorithm,
                digest = provenance.ScenarioFixtureDigest
            }
        }, cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(Path.Combine(root, "decision.json"), new
        {
            schema_version = 2,
            execution_mode = "SIMULATED",
            status = replay.Passed ? "SYNTHETIC_CONFIRMED" : "SYNTHETIC_FLAKY",
            provenance,
            replay,
            minimization = new
            {
                original_operations = minimization.Original.Value.Operations.Count,
                minimized_operations = minimization.Minimized.Value.Operations.Count,
                operation_reduction = minimization.OperationReduction,
                original_bytes = minimization.Original.CanonicalUtf8.Length,
                minimized_bytes = minimization.Minimized.CanonicalUtf8.Length,
                byte_reduction = minimization.ByteReduction,
                maximum_oracle_attempts = provenance.MaximumOracleAttempts,
                oracle_attempts = minimization.OracleAttempts,
                stop_reason = minimization.StopReason
            },
            limitations = SimulationLimitations
        }, cancellationToken).ConfigureAwait(false);

        await WriteBytesAsync(Path.Combine(root, "inputs", "original.case.json"), original.CanonicalUtf8, cancellationToken).ConfigureAwait(false);
        await WriteBytesAsync(Path.Combine(root, "inputs", "minimized.case.json"), minimization.Minimized.CanonicalUtf8, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(root, "runs", "discovery.run.json"), new
        {
            schema_version = 1,
            execution_mode = "SIMULATED",
            run_id = runId,
            campaign_id = campaign.CampaignId,
            case_id = original.CaseId,
            scenario = ReadScenario(campaign),
            result_class = campaign.ResultClass,
            virtual_duration_ms = campaign.Events.Count == 0 ? 0 : campaign.Events[^1].VirtualElapsedMs,
            signature = campaign.Signature
        }, cancellationToken).ConfigureAwait(false);

        foreach (var replayAttempt in replay.Attempts)
        {
            await WriteJsonAsync(Path.Combine(root, "runs", $"replay-{replayAttempt.Attempt:D2}.run.json"), new
            {
                schema_version = 1,
                execution_mode = "SIMULATED",
                attempt = replayAttempt.Attempt,
                classification = replayAttempt.Classification,
                observed_signature = replayAttempt.ObservedSignature,
                reset_policy = "SIMULATED_CLEAN"
            }, cancellationToken).ConfigureAwait(false);
        }

        var artifactName = SafeRelativePath.Normalize(campaign.Artifact.RelativeName);
        await WriteBytesAsync(Path.Combine(root, artifactName.Replace('/', Path.DirectorySeparatorChar)), campaign.Artifact.Bytes, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(root, "crash", "analysis.json"), campaign.Analysis, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(Path.Combine(root, "runs", "campaign-events.json"), campaign.Events, cancellationToken).ConfigureAwait(false);

        var store = new ContentAddressedStore(Path.Combine(root, "cas"));
        await using (var source = new MemoryStream(campaign.Artifact.Bytes, writable: false))
        {
            _ = await store.PutAsync(source, cancellationToken).ConfigureAwait(false);
        }

        var report = BuildReport(findingId, campaign, original, minimization, replay);
        await WriteBytesAsync(Path.Combine(root, "report", "index.html"), ArtifactText.Encode(report), cancellationToken).ConfigureAwait(false);
        var entries = await EvidenceManifest.CreateAsync(root, cancellationToken).ConfigureAwait(false);
        return new EvidenceBuildResult(root, Path.Combine(root, EvidenceManifest.FileName), findingId.ToString("D"), campaign.Signature, entries.Count);
    }

    private static string ReadScenario(CampaignRunResult campaign)
    {
        return campaign.Scenario;
    }

    private static bool MatchesSimulationEnvironment(
        CapabilityReport report,
        MinimizationReplayProvenance provenance)
    {
        return report.ExecutionMode == "SIMULATED"
            && report.ObservedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                == SimulationEvidenceContract.VirtualEpochUtc
            && report.Evidence.TryGetValue("backend_contract", out var backend)
            && backend == SimulationEvidenceContract.BackendId
            && report.Evidence.TryGetValue("simulator_version", out var simulatorVersion)
            && simulatorVersion == SimulationEvidenceContract.SimulatorVersion
            && report.Evidence.TryGetValue("scenario_fixture_name", out var scenario)
            && scenario == provenance.Scenario
            && report.Evidence.TryGetValue("scenario_fixture_schema_version", out var schemaVersion)
            && schemaVersion == provenance.ScenarioFixtureSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && report.Evidence.TryGetValue("scenario_fixture_digest_algorithm", out var digestAlgorithm)
            && digestAlgorithm == provenance.ScenarioFixtureDigestAlgorithm
            && report.Evidence.TryGetValue("scenario_fixture_digest", out var digest)
            && digest == provenance.ScenarioFixtureDigest;
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        var bytes = ArtifactText.SerializeJson(value, ContractJson.Indented);
        await WriteBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Output path has no parent."));
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65_536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string BuildReport(
        Guid findingId,
        CampaignRunResult campaign,
        CanonicalCase original,
        MinimizationResult minimization,
        ReplayDecision replay)
    {
        var signature = WebUtility.HtmlEncode(campaign.Signature);
        return FormattableString.Invariant($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>KCrashLab simulated finding {{findingId:D}}</title>
              <style>
                body{font-family:Segoe UI,system-ui,sans-serif;max-width:900px;margin:40px auto;padding:0 20px;color:#16202a;background:#f5f7fa}
                .banner{background:#8b1e1e;color:white;padding:18px 22px;border-radius:8px;font-weight:700;letter-spacing:.04em}
                main{background:white;margin-top:18px;padding:28px;border-radius:8px;box-shadow:0 8px 24px #10203018}
                dt{font-weight:700;margin-top:14px} dd{margin:4px 0 0 0} code{overflow-wrap:anywhere}
              </style>
            </head>
            <body>
              <div class="banner">SIMULATED — NOT A REAL KERNEL CRASH</div>
              <main>
                <h1>KCrashLab reproducibility finding</h1>
                <dl>
                  <dt>Finding</dt><dd>{{findingId:D}}</dd>
                  <dt>Signature v1</dt><dd><code>{{signature}}</code></dd>
                  <dt>Case ID</dt><dd><code>{{original.CaseId}}</code></dd>
                  <dt>Sequence minimization</dt><dd>{{original.Value.Operations.Count}} → {{minimization.Minimized.Value.Operations.Count}} operations ({{minimization.OperationReduction:P0}} reduction)</dd>
                  <dt>Case byte minimization</dt><dd>{{original.CanonicalUtf8.Length}} → {{minimization.Minimized.CanonicalUtf8.Length}} canonical bytes ({{minimization.ByteReduction:P0}} reduction)</dd>
                  <dt>Simulated clean replay</dt><dd>{{replay.MatchingAttempts}}/{{replay.EligibleAttempts}} matching; policy {{replay.Policy.RequiredMatches}}/{{replay.Policy.Attempts}}; {{(replay.Passed ? "PASS" : "FAIL")}}</dd>
                  <dt>Kernel crash</dt><dd>NOT CLAIMED</dd>
                  <dt>Root cause</dt><dd>NOT CLAIMED</dd>
                  <dt>Exploitability</dt><dd>NOT ASSESSED</dd>
                </dl>
              </main>
            </body>
            </html>
            """);
    }
}
