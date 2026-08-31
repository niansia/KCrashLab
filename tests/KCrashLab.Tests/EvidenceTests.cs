using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class EvidenceTests
{
    [Fact]
    public async Task EndToEndBundleVerifiesAndDetectsCorruption()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var fixture = await ScenarioFixtureLoader.LoadAsync(TestPaths.Sample("scenarios", "dump-ready.json"), CancellationToken.None);
            var original = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));
            var campaignId = DeterministicIdentity.CreateGuid("campaign", fixture.Name, original.CaseId, fixture.Seed);
            var store = new SqliteCampaignEventStore(Path.Combine(temporary, "events.db"));
            await store.InitializeAsync(CancellationToken.None);
            var result = await new CampaignOrchestrator(store, new SimulatedLabBackend(fixture)).RunAsync(
                new CampaignSpec(campaignId, fixture.Name, fixture.Seed),
                original,
                null,
                CancellationToken.None);
            Assert.Equal(CampaignState.Complete, result.State);

            const int maximumOracleAttempts = 128;
            var minimization = await HierarchicalMinimizer.MinimizeAsync(
                original,
                result.Signature!,
                static (candidate, _) => Task.FromResult(SyntheticStateTarget.Evaluate(candidate)),
                maximumOracleAttempts,
                CancellationToken.None);
            var replay = await ReplayEngine.RunAsync(
                new ReplayPolicy(3, 3),
                (_, _) => Task.FromResult(SyntheticStateTarget.Evaluate(minimization.Minimized)),
                result.Signature!,
                CancellationToken.None);
            var bundle = Path.Combine(temporary, "bundle");
            var provenance = ExperimentProvenanceBuilder.UnspecifiedForMinimizationReplay(
                fixture,
                original,
                result.Signature!,
                maximumOracleAttempts,
                replay.Policy);
            await EvidenceBundleBuilder.BuildAsync(bundle, result, original, minimization, replay, provenance, CancellationToken.None);

            var secondBundle = Path.Combine(temporary, "bundle-second");
            await EvidenceBundleBuilder.BuildAsync(secondBundle, result, original, minimization, replay, provenance, CancellationToken.None);
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(bundle, EvidenceManifest.FileName)),
                await File.ReadAllTextAsync(Path.Combine(secondBundle, EvidenceManifest.FileName)));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(bundle, "report", "index.html"));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(bundle, "decision.json"));
            using (var environment = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(bundle, "environment.json"))))
            {
                Assert.Equal(2, environment.RootElement.GetProperty("schema_version").GetInt32());
                Assert.Equal(SimulationEvidenceContract.BackendId, environment.RootElement.GetProperty("backend").GetString());
                Assert.False(environment.RootElement.TryGetProperty("capability_report", out _));
                Assert.Equal(
                    ScenarioFixtureIdentity.DefinitionDigest(fixture),
                    environment.RootElement.GetProperty("scenario_fixture").GetProperty("digest").GetString());
            }
            Assert.Equal("VIRTUAL", result.CapabilityReport.Host.Architecture);

            var valid = await EvidenceBundleVerifier.VerifyAsync(bundle, CancellationToken.None);
            Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Errors));

            var untrackedPath = Path.Combine(bundle, "untracked.txt");
            await File.WriteAllTextAsync(untrackedPath, "not in manifest");
            var untracked = await EvidenceBundleVerifier.VerifyAsync(bundle, CancellationToken.None);
            Assert.False(untracked.IsValid);
            Assert.Contains(untracked.Errors, static error => error.Contains("Untracked evidence file", StringComparison.Ordinal));
            File.Delete(untrackedPath);

            await File.AppendAllTextAsync(Path.Combine(bundle, "finding.json"), " ");
            var corrupt = await EvidenceBundleVerifier.VerifyAsync(bundle, CancellationToken.None);
            Assert.False(corrupt.IsValid);
            Assert.Contains(corrupt.Errors, static error => error.Contains("Hash mismatch", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
