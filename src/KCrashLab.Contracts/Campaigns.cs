namespace KCrashLab.Contracts;

public enum CampaignState
{
    Created,
    Validating,
    Acquiring,
    Ready,
    Dispatching,
    Running,
    CaseDone,
    SuspectedFailure,
    Collecting,
    Triaging,
    Queued,
    Minimizing,
    Complete,
    BlockedByEnvironment,
    InfraFailed,
    Cancelled,
    Quarantined
}

public sealed record CampaignEvent(
    int SchemaVersion,
    Guid EventId,
    Guid CampaignId,
    long Sequence,
    CampaignState FromState,
    CampaignState ToState,
    string Reason,
    string Actor,
    DateTimeOffset OccurredUtc,
    long VirtualElapsedMs,
    Guid CorrelationId);

public interface ICampaignEventStore
{
    Task<IReadOnlyList<CampaignEvent>> ReadAsync(Guid campaignId, CancellationToken cancellationToken);

    Task<bool> AppendAsync(CampaignEvent campaignEvent, CancellationToken cancellationToken);
}

