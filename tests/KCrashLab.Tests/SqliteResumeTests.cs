using KCrashLab.Contracts;
using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class SqliteResumeTests
{
    [Fact]
    public async Task ResumeFromRunningDoesNotDispatchAgain()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var fixture = await ScenarioFixtureLoader.LoadAsync(TestPaths.Sample("scenarios", "dump-ready.json"), CancellationToken.None);
            var testCase = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));
            var campaignId = DeterministicIdentity.CreateGuid("resume-test", testCase.CaseId);
            var spec = new CampaignSpec(campaignId, fixture.Name, fixture.Seed);
            var store = new SqliteCampaignEventStore(Path.Combine(temporary, "events.db"));
            await store.InitializeAsync(CancellationToken.None);

            var firstBackend = new SimulatedLabBackend(fixture);
            var first = await new CampaignOrchestrator(store, firstBackend).RunAsync(spec, testCase, CampaignState.Running, CancellationToken.None);
            Assert.True(first.PausedByTestHook);
            Assert.Equal(1, firstBackend.DispatchCount);

            var resumedBackend = new SimulatedLabBackend(fixture);
            var resumed = await new CampaignOrchestrator(store, resumedBackend).RunAsync(spec, testCase, null, CancellationToken.None);
            Assert.Equal(CampaignState.Complete, resumed.State);
            Assert.Equal(0, resumedBackend.DispatchCount);
            Assert.NotNull(resumed.Signature);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}

