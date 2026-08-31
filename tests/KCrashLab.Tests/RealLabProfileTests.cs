using KCrashLab.Contracts;
using KCrashLab.Simulation;

namespace KCrashLab.Tests;

public sealed class RealLabProfileTests
{
    [Fact]
    public void AcceptsOnlyPinnedDisposableIsolatedLabProfile()
    {
        var valid = ValidProfile();
        Assert.True(RealLabProfileValidator.Validate(valid).IsValid);

        var unsafeProfile = valid with
        {
            DevicePath = @"\\.\ArbitraryDevice",
            DeviceInterfaceGuid = "00000000-0000-0000-0000-000000000000",
            ExchangeRoot = @"C:\Users\researcher",
            DisposableLab = false,
            NetworkIsolated = false,
            ImmutableCheckpoint = false
        };
        var result = RealLabProfileValidator.Validate(unsafeProfile);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static error => error.Contains("device_path", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static error => error.Contains("device_interface_guid", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static error => error.Contains("exchange_root", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static error => error.Contains("disposable_lab", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static error => error.Contains("network_isolated", StringComparison.Ordinal));
        Assert.Contains(result.Errors, static error => error.Contains("immutable_checkpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void DumpRequiresPositiveUnchangedLengthAndExclusiveRead()
    {
        var tracker = new DumpStabilityTracker(TimeSpan.FromSeconds(10));
        var start = DateTimeOffset.Parse("2026-08-31T00:00:00Z");

        Assert.Equal(DumpReadiness.Missing, tracker.Observe(start, null, false));
        Assert.Equal(DumpReadiness.Growing, tracker.Observe(start, 1_024, false));
        Assert.Equal(DumpReadiness.Growing, tracker.Observe(start.AddSeconds(15), 2_048, false));
        Assert.Equal(DumpReadiness.Growing, tracker.Observe(start.AddSeconds(20), 2_048, false));
        Assert.Equal(DumpReadiness.Growing, tracker.Observe(start.AddSeconds(25), 2_048, true));
        Assert.Equal(DumpReadiness.Stable, tracker.Observe(start.AddSeconds(36), 2_048, true));
    }

    private static RealLabProfile ValidProfile() => new(
        1,
        "REAL_LAB",
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        "Windows 11 24H2 build 26100",
        string.Concat(Enumerable.Repeat("ab", 32)),
        "KCrashLabTarget.sys",
        @"\\.\KCrashLabTarget",
        "4fd15d37-1f06-4e50-a823-376ad418f196",
        @"D:\KCrashLabExchange",
        "dumps/MEMORY.DMP",
        "lab-ticket-20260831",
        true,
        true,
        true,
        30,
        15);
}
