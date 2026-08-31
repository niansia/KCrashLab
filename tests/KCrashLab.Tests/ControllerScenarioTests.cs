using KCrashLab.Contracts;
using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class ControllerScenarioTests
{
    public static TheoryData<string, CampaignState, string> Scenarios => new()
    {
        { "complete", CampaignState.Complete, "COMPLETE" },
        { "operation-timeout", CampaignState.Complete, "OPERATION_TIMEOUT" },
        { "agent-loss", CampaignState.Complete, "AGENT_LOSS" },
        { "reboot", CampaignState.Complete, "REBOOT" },
        { "dump-ready", CampaignState.Complete, "SYNTHETIC_FAILURE" },
        { "corrupt-artifact", CampaignState.Quarantined, "QUARANTINED" },
        { "infra-failure", CampaignState.InfraFailed, "INFRASTRUCTURE_ERROR" },
        { "flaky-signature", CampaignState.Complete, "SYNTHETIC_FAILURE" }
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task ControllerClassifiesEveryScriptedScenario(
        string scenario,
        CampaignState expectedState,
        string expectedResultClass)
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var fixture = await ScenarioFixtureLoader.LoadAsync(TestPaths.Sample("scenarios", scenario + ".json"), CancellationToken.None);
            var testCase = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));
            var campaignId = DeterministicIdentity.CreateGuid("scenario-controller-test", scenario, testCase.CaseId);
            var store = new SqliteCampaignEventStore(Path.Combine(temporary, "events.db"));
            await store.InitializeAsync(CancellationToken.None);

            var result = await new CampaignOrchestrator(store, new SimulatedLabBackend(fixture)).RunAsync(
                new CampaignSpec(campaignId, scenario, fixture.Seed),
                testCase,
                null,
                CancellationToken.None);

            Assert.Equal(expectedState, result.State);
            Assert.Equal(expectedResultClass, result.ResultClass);
            if (expectedState == CampaignState.Complete && scenario != "complete")
            {
                Assert.NotNull(result.Analysis);
                Assert.NotNull(result.Signature);
            }
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }
}

