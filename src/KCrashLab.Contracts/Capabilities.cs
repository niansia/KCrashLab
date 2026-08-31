namespace KCrashLab.Contracts;

public enum CapabilityStatus
{
    Available,
    Unavailable,
    Unverified,
    Blocked
}

public sealed record HostDescription(
    string OsDescription,
    string OsVersion,
    string Architecture,
    string? WindowsEdition);

public sealed record CapabilitySet(
    CapabilityStatus Hypervisor,
    CapabilityStatus HypervManagement,
    CapabilityStatus WindowsSdk,
    CapabilityStatus WdkDriverTargets,
    CapabilityStatus DisposableKernelLab);

public sealed record CapabilityReport(
    int SchemaVersion,
    string ExecutionMode,
    DateTimeOffset ObservedAtUtc,
    HostDescription Host,
    CapabilitySet Capabilities,
    CapabilityStatus RealKernelCampaign,
    IReadOnlyList<string> Reasons,
    IReadOnlyDictionary<string, string> Evidence);

