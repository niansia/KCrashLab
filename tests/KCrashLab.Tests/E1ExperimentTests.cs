using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class E1ExperimentTests
{
    [Fact]
    public void AblationChangesOnlyAdmissionAndParentSelectionFactors()
    {
        var arms = new[]
        {
            FuzzPolicySet.KeepAllUniform(),
            FuzzPolicySet.KeepAllEnergyRanked(),
            FuzzPolicySet.NoveltyUniform(),
            FuzzPolicySet.NoveltyEnergyRanked()
        };

        Assert.Equal(4, arms.Select(static arm => arm.StrategyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, arms.Select(static arm => arm.CorpusAdmission.PolicyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, arms.Select(static arm => arm.ParentSelection.PolicyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Single(arms.Select(static arm => arm.OperatorSelection.PolicyId).Distinct(StringComparer.Ordinal));
        Assert.Single(arms.Select(static arm => arm.CandidateSelection.PolicyId).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PairedExperimentIsDeterministicVerifiableAndTamperEvident()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
            var result = await E1ExperimentRunner.RunAsync(
                seed,
                budgetPerTrial: 128,
                trialsPerStrategy: 4,
                baseCampaignSeed: 20260831,
                static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
                CancellationToken.None);

            Assert.Equal(16, result.Trials.Count);
            Assert.Equal(4, result.Strategies.Count);
            Assert.Equal(4, result.FactorialContrasts.Count);
            Assert.All(result.Trials.GroupBy(static trial => trial.Trial), static pair =>
            {
                Assert.Equal(4, pair.Count());
                Assert.Single(pair.Select(static trial => trial.CampaignSeed).Distinct());
            });

            var first = Path.Combine(temporary, "first");
            var second = Path.Combine(temporary, "second");
            await E1ExperimentArtifacts.BuildAsync(first, result, CancellationToken.None);
            await E1ExperimentArtifacts.BuildAsync(second, result, CancellationToken.None);
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(first, EvidenceManifest.FileName)),
                await File.ReadAllTextAsync(Path.Combine(second, EvidenceManifest.FileName)));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "raw.csv"));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "survival.csv"));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "report", "index.html"));

            var valid = await E1ExperimentArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Errors));

            await File.AppendAllTextAsync(Path.Combine(first, "raw.csv"), "tampered");
            var corrupt = await E1ExperimentArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.False(corrupt.IsValid);
            Assert.Contains(corrupt.Errors, static error => error.Contains("Hash mismatch", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}
