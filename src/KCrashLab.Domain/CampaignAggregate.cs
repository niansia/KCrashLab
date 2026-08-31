using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed class CampaignAggregate
{
    private static readonly Dictionary<CampaignState, HashSet<CampaignState>> Allowed =
        new Dictionary<CampaignState, HashSet<CampaignState>>
        {
            [CampaignState.Created] = [CampaignState.Validating],
            [CampaignState.Validating] = [CampaignState.Acquiring, CampaignState.BlockedByEnvironment],
            [CampaignState.Acquiring] = [CampaignState.Ready],
            [CampaignState.Ready] = [CampaignState.Dispatching],
            [CampaignState.Dispatching] = [CampaignState.Running],
            [CampaignState.Running] = [CampaignState.CaseDone, CampaignState.SuspectedFailure],
            [CampaignState.CaseDone] = [CampaignState.Collecting, CampaignState.Complete],
            [CampaignState.SuspectedFailure] = [CampaignState.Collecting],
            [CampaignState.Collecting] = [CampaignState.Triaging, CampaignState.Quarantined],
            [CampaignState.Triaging] = [CampaignState.Queued, CampaignState.Minimizing, CampaignState.Complete],
            [CampaignState.Queued] = [CampaignState.Dispatching, CampaignState.Minimizing, CampaignState.Complete],
            [CampaignState.Minimizing] = [CampaignState.Complete, CampaignState.Quarantined],
            [CampaignState.InfraFailed] = [CampaignState.Validating, CampaignState.Quarantined],
            [CampaignState.BlockedByEnvironment] = [],
            [CampaignState.Complete] = [],
            [CampaignState.Cancelled] = [],
            [CampaignState.Quarantined] = []
        };

    private readonly List<CampaignEvent> events = [];

    public CampaignAggregate(Guid campaignId)
    {
        if (campaignId == Guid.Empty)
        {
            throw new ArgumentException("Campaign ID cannot be empty.", nameof(campaignId));
        }

        CampaignId = campaignId;
    }

    public Guid CampaignId { get; }

    public CampaignState State { get; private set; } = CampaignState.Created;

    public long Sequence => events.Count;

    public IReadOnlyList<CampaignEvent> Events => events;

    public CampaignEvent Transition(
        CampaignState toState,
        string reason,
        string actor,
        DateTimeOffset occurredUtc,
        long virtualElapsedMs,
        Guid correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentOutOfRangeException.ThrowIfNegative(virtualElapsedMs);

        if (!IsAllowed(State, toState))
        {
            throw new InvalidOperationException($"Transition {State} -> {toState} is not allowed.");
        }

        var nextSequence = Sequence + 1;
        var campaignEvent = new CampaignEvent(
            1,
            DeterministicIdentity.CreateGuid("campaign-event", CampaignId, nextSequence, State, toState, correlationId),
            CampaignId,
            nextSequence,
            State,
            toState,
            reason,
            actor,
            occurredUtc,
            virtualElapsedMs,
            correlationId);
        Apply(campaignEvent);
        return campaignEvent;
    }

    public static CampaignAggregate Rehydrate(Guid campaignId, IEnumerable<CampaignEvent> history)
    {
        var aggregate = new CampaignAggregate(campaignId);
        foreach (var campaignEvent in history.OrderBy(static item => item.Sequence))
        {
            aggregate.Apply(campaignEvent);
        }

        return aggregate;
    }

    public static bool IsTerminal(CampaignState state) =>
        state is CampaignState.Complete or CampaignState.BlockedByEnvironment or CampaignState.Cancelled or CampaignState.Quarantined;

    private static bool IsAllowed(CampaignState fromState, CampaignState toState)
    {
        if (toState is CampaignState.Cancelled or CampaignState.InfraFailed)
        {
            return !IsTerminal(fromState);
        }

        if (toState == CampaignState.Quarantined)
        {
            return !IsTerminal(fromState);
        }

        return Allowed.TryGetValue(fromState, out var next) && next.Contains(toState);
    }

    private void Apply(CampaignEvent campaignEvent)
    {
        if (campaignEvent.CampaignId != CampaignId)
        {
            throw new InvalidDataException("Campaign event belongs to another campaign.");
        }

        if (campaignEvent.SchemaVersion != 1 || campaignEvent.Sequence != Sequence + 1 || campaignEvent.FromState != State)
        {
            throw new InvalidDataException("Campaign event history is not contiguous.");
        }

        if (!IsAllowed(State, campaignEvent.ToState))
        {
            throw new InvalidDataException($"Stored transition {State} -> {campaignEvent.ToState} is invalid.");
        }

        events.Add(campaignEvent);
        State = campaignEvent.ToState;
    }
}
