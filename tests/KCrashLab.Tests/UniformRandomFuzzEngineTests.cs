using KCrashLab.Domain;
using KCrashLab.Simulation;

namespace KCrashLab.Tests;

public sealed class UniformRandomFuzzEngineTests
{
    [Fact]
    public async Task SameSeedProducesSameUniformRandomCampaign()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var engine = new UniformRandomFuzzEngine(DefaultMutationOperators.Create());

        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal(256, first.Executions);
        Assert.Equal("KEEP_ALL_UNIFORM_V2", first.Strategy);
        Assert.Equal(first.ExecutionLog.Select(static item => item.CaseId), second.ExecutionLog.Select(static item => item.CaseId));
        Assert.Equal(first.ExecutionLog.Select(static item => item.Signature), second.ExecutionLog.Select(static item => item.Signature));
        Assert.Equal(first.GlobalCoverage, second.GlobalCoverage);
        Assert.All(first.Corpus.Skip(1), entry => Assert.Contains(
            first.Corpus,
            candidate => candidate.TestCase.CaseId == entry.TestCase.Value.ParentCaseId));

        Task<KCrashLab.Contracts.FuzzCampaignResult> RunAsync() => engine.RunAsync(
            seed,
            budget: 256,
            campaignSeed: 20260831,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            CancellationToken.None);
    }
}
