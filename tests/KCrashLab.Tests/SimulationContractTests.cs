using KCrashLab.Contracts;
using KCrashLab.Domain;
using KCrashLab.Simulation;

namespace KCrashLab.Tests;

public sealed class SimulationContractTests
{
    public static TheoryData<string> Scenarios => new()
    {
        "complete",
        "operation-timeout",
        "agent-loss",
        "reboot",
        "dump-ready",
        "corrupt-artifact",
        "infra-failure",
        "flaky-signature"
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task EveryScenarioIsDeterministicAndExplicitlySimulated(string scenario)
    {
        var fixture = await ScenarioFixtureLoader.LoadAsync(TestPaths.Sample("scenarios", scenario + ".json"), CancellationToken.None);
        var backend = new SimulatedLabBackend(fixture);
        var campaignId = DeterministicIdentity.CreateGuid("test-campaign", scenario);
        var lease = await backend.AcquireAsync(new CampaignSpec(campaignId, scenario, fixture.Seed), CancellationToken.None);
        var first = new List<LabEvent>();
        await foreach (var item in backend.ObserveAsync(lease, CancellationToken.None))
        {
            first.Add(item);
        }

        await backend.ResetAsync(lease, ResetPolicy.SimulatedClean, CancellationToken.None);
        var second = new List<LabEvent>();
        await foreach (var item in backend.ObserveAsync(lease, CancellationToken.None))
        {
            second.Add(item);
        }

        Assert.Equal("SIMULATED", fixture.ExecutionMode);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task HyperVBackendAlwaysFailsClosed()
    {
        var backend = new HyperVLabBackend();
        var spec = new CampaignSpec(Guid.NewGuid(), "dump-ready", 1);

        await Assert.ThrowsAsync<BackendBlockedException>(() => backend.AcquireAsync(spec, CancellationToken.None));
    }
}

