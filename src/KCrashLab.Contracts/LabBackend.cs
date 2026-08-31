namespace KCrashLab.Contracts;

public enum LabEventKind
{
    Ready,
    Running,
    Heartbeat,
    CaseDone,
    OperationTimeout,
    AgentLoss,
    BootIdChanged,
    FailureDetected,
    ArtifactGrowth,
    ArtifactStable,
    CorruptArtifact,
    InfrastructureFailure,
    FlakySignature
}

public enum ResetPolicy
{
    SimulatedClean
}

public sealed record CampaignSpec(Guid CampaignId, string Scenario, long Seed);

public sealed record LabLease(Guid LeaseId, Guid CampaignId, string Backend, string BootId);

public sealed record DispatchReceipt(Guid DispatchId, Guid RunId, string CaseId, bool Duplicate);

public sealed record LabEvent(
    Guid EventId,
    LabEventKind Kind,
    long VirtualTimestampMs,
    string BootId,
    string? Detail,
    string? FailureId,
    string? ExpectedSha256,
    long? ArtifactLength);

public sealed record FailureRef(string FailureId, string Scenario);

public sealed record ArtifactReceipt(
    string RelativeName,
    byte[] Bytes,
    string Sha256,
    bool IsStable,
    bool IsSynthetic);

public interface ILabBackend
{
    Task<CapabilityReport> ProbeAsync(CancellationToken cancellationToken);

    Task<LabLease> AcquireAsync(CampaignSpec spec, CancellationToken cancellationToken);

    Task<DispatchReceipt> DispatchAsync(
        LabLease lease,
        CanonicalCase testCase,
        CancellationToken cancellationToken);

    IAsyncEnumerable<LabEvent> ObserveAsync(
        LabLease lease,
        CancellationToken cancellationToken);

    Task<ArtifactReceipt> CollectAsync(
        LabLease lease,
        FailureRef failure,
        CancellationToken cancellationToken);

    Task ResetAsync(LabLease lease, ResetPolicy policy, CancellationToken cancellationToken);

    Task ReleaseAsync(LabLease lease, CancellationToken cancellationToken);
}

public sealed class BackendBlockedException(string message) : InvalidOperationException(message);

