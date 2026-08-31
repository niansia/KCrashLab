using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class CampaignStateTests
{
    [Fact]
    public void InvalidTransitionFailsClosed()
    {
        var campaign = new CampaignAggregate(Guid.Parse("52e6c25d-5af4-46e0-a4a8-e3765398ae84"));

        Assert.Throws<InvalidOperationException>(() => campaign.Transition(
            CampaignState.Running,
            "skip validation",
            "test",
            DateTimeOffset.UnixEpoch,
            0,
            Guid.Parse("83845b85-34ab-49e7-b8dd-bd858be1d061")));
    }
}

