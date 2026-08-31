using KCrashLab.Contracts;
using KCrashLab.Domain;
using KCrashLab.Simulation;

namespace KCrashLab.Tests;

public sealed class FuzzEngineTests
{
    [Fact]
    public async Task SafeSeedDiscoversKnownSyntheticFindingWithinFixedBudget()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        Assert.Null(SyntheticStateTarget.Evaluate(seed));
        var engine = new DeterministicFuzzEngine(DefaultMutationOperators.Create());

        var result = await engine.RunAsync(
            seed,
            budget: 256,
            campaignSeed: 20260831,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(SyntheticStateTarget.KnownSignature, finding.Signature);
        Assert.InRange(finding.FirstExecution, 2, 256);
        Assert.True(result.GlobalCoverage.Count > 5);
        Assert.True(result.Corpus.Count > 1);
        Assert.All(result.Findings, static item => Assert.True(item.Occurrences >= 1));
    }

    [Fact]
    public async Task SameSeedProducesSameExecutionOrderAndFinding()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var engine = new DeterministicFuzzEngine(DefaultMutationOperators.Create());

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(first.ExecutionLog.Select(static item => item.CaseId), second.ExecutionLog.Select(static item => item.CaseId));
        Assert.Equal(first.ExecutionLog.Select(static item => item.Signature), second.ExecutionLog.Select(static item => item.Signature));
        Assert.Equal(first.GlobalCoverage, second.GlobalCoverage);
        Assert.Equal(first.Findings.Select(static item => item.Signature), second.Findings.Select(static item => item.Signature));

        Task<FuzzCampaignResult> RunAsync() => engine.RunAsync(
            seed,
            budget: 256,
            campaignSeed: 20260831,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None);
    }

    [Fact]
    public async Task NegativeCampaignSeedFailsClosed()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var engine = new DeterministicFuzzEngine(DefaultMutationOperators.Create());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.RunAsync(
            seed,
            budget: 1,
            campaignSeed: -1,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None));
    }

    [Fact]
    public async Task DifferentCampaignSeedsChangeSchedulingOrder()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var engine = new DeterministicFuzzEngine(DefaultMutationOperators.Create());

        var first = await engine.RunAsync(
            seed,
            budget: 64,
            campaignSeed: 1,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None);
        var second = await engine.RunAsync(
            seed,
            budget: 64,
            campaignSeed: 2,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None);

        Assert.NotEqual(
            first.ExecutionLog.Select(static execution => execution.CaseId),
            second.ExecutionLog.Select(static execution => execution.CaseId));
    }
}
