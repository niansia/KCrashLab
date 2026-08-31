using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class E2ExperimentRunnerTests
{
    [Fact]
    public async Task StatefulModeCanReachCrossOperationFindingButSingleCallCannot()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-reset-seed.case.json")));

        var result = await E2ExperimentRunner.RunAsync(
            seed,
            budgetPerTrial: 512,
            trialsPerMode: 4,
            baseCampaignSeed: 20260831,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None);

        var singleCall = result.Modes.Single(static mode => mode.Mode == "SINGLE_CALL");
        var stateful = result.Modes.Single(static mode => mode.Mode == "STATEFUL");
        Assert.Equal(0, singleCall.Discoveries);
        Assert.Equal(4, singleCall.CensoredTrials);
        Assert.True(stateful.Discoveries > 0);
        Assert.All(
            result.Trials.Where(static trial => trial.Mode == "SINGLE_CALL"),
            static trial => Assert.True(trial.MaximumSequenceLength == 1 && !trial.Found));
        Assert.Equal(4, result.PairedOutcomes.StatefulOnly + result.PairedOutcomes.NeitherDiscovered);
    }

    [Fact]
    public async Task E2ArtifactsAreDeterministicVerifiableAndTamperEvident()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-reset-seed.case.json")));
            var result = await E2ExperimentRunner.RunAsync(
                seed,
                budgetPerTrial: 512,
                trialsPerMode: 4,
                baseCampaignSeed: 20260831,
                static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
                CancellationToken.None);
            var first = Path.Combine(temporary, "first");
            var second = Path.Combine(temporary, "second");
            await E2ExperimentArtifacts.BuildAsync(first, result, CancellationToken.None);
            await E2ExperimentArtifacts.BuildAsync(second, result, CancellationToken.None);
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(first, EvidenceManifest.FileName)),
                await File.ReadAllTextAsync(Path.Combine(second, EvidenceManifest.FileName)));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "raw.csv"));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "report", "index.html"));

            var valid = await E2ExperimentArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Errors));

            await File.AppendAllTextAsync(Path.Combine(first, "raw.csv"), "tampered");
            var corrupt = await E2ExperimentArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.False(corrupt.IsValid);
            Assert.Contains(corrupt.Errors, static error => error.Contains("Hash mismatch", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
