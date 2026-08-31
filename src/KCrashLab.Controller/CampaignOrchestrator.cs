using System.Security.Cryptography;
using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Controller;

public sealed record CampaignRunResult(
    Guid CampaignId,
    string Scenario,
    CampaignState State,
    CapabilityReport CapabilityReport,
    string ResultClass,
    ArtifactReceipt? Artifact,
    TriageAnalysis? Analysis,
    string? Signature,
    IReadOnlyList<CampaignEvent> Events,
    bool PausedByTestHook);

public sealed class CampaignOrchestrator(
    ICampaignEventStore eventStore,
    ILabBackend backend)
{
    private static readonly DateTimeOffset DeterministicEpoch = DateTimeOffset.Parse(
        "2026-08-31T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    public async Task<CampaignRunResult> RunAsync(
        CampaignSpec spec,
        CanonicalCase testCase,
        CampaignState? stopAfterState,
        CancellationToken cancellationToken)
    {
        var history = await eventStore.ReadAsync(spec.CampaignId, cancellationToken).ConfigureAwait(false);
        var campaign = CampaignAggregate.Rehydrate(spec.CampaignId, history);
        var capability = await backend.ProbeAsync(cancellationToken).ConfigureAwait(false);
        var correlationId = DeterministicIdentity.CreateGuid("campaign-correlation", spec.CampaignId);
        LabLease? lease = null;
        ArtifactReceipt? artifact = null;
        TriageAnalysis? analysis = null;
        string? signature = null;
        var resultClass = "INCOMPLETE";

        while (!CampaignAggregate.IsTerminal(campaign.State))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (campaign.State)
            {
                case CampaignState.Created:
                    await PersistTransitionAsync(campaign, CampaignState.Validating, "validate backend capability", 0, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case CampaignState.Validating:
                    if (capability.ExecutionMode != "SIMULATED")
                    {
                        await PersistTransitionAsync(campaign, CampaignState.BlockedByEnvironment, "v1 requires explicit SIMULATED execution mode", 0, correlationId, cancellationToken).ConfigureAwait(false);
                        resultClass = "BLOCKED_BY_ENVIRONMENT";
                        break;
                    }

                    await PersistTransitionAsync(campaign, CampaignState.Acquiring, "simulator capability contract accepted", 0, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case CampaignState.Acquiring:
                    lease = await backend.AcquireAsync(spec, cancellationToken).ConfigureAwait(false);
                    await PersistTransitionAsync(campaign, CampaignState.Ready, "deterministic simulated lease acquired", 0, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case CampaignState.Ready:
                    lease ??= await backend.AcquireAsync(spec, cancellationToken).ConfigureAwait(false);
                    await PersistTransitionAsync(campaign, CampaignState.Dispatching, "case dispatch prepared", 0, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case CampaignState.Dispatching:
                    lease ??= await backend.AcquireAsync(spec, cancellationToken).ConfigureAwait(false);
                    _ = await backend.DispatchAsync(lease, testCase, cancellationToken).ConfigureAwait(false);
                    await PersistTransitionAsync(campaign, CampaignState.Running, "idempotent dispatch acknowledged", 0, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case CampaignState.Running:
                case CampaignState.SuspectedFailure:
                    lease ??= await backend.AcquireAsync(spec, cancellationToken).ConfigureAwait(false);
                    var observed = await ObserveAsync(campaign, lease, spec, correlationId, cancellationToken).ConfigureAwait(false);
                    artifact = observed.Artifact;
                    analysis = observed.Analysis;
                    signature = observed.Signature;
                    resultClass = observed.ResultClass;
                    break;

                case CampaignState.CaseDone:
                    await PersistTransitionAsync(campaign, CampaignState.Complete, "successful synthetic case completed", CurrentElapsed(campaign), correlationId, cancellationToken).ConfigureAwait(false);
                    resultClass = "COMPLETE";
                    break;

                case CampaignState.Collecting:
                case CampaignState.Triaging:
                case CampaignState.Queued:
                case CampaignState.Minimizing:
                    await PersistTransitionAsync(campaign, CampaignState.Quarantined, "resume metadata is incomplete for this state", CurrentElapsed(campaign), correlationId, cancellationToken).ConfigureAwait(false);
                    resultClass = "QUARANTINED";
                    break;

                case CampaignState.InfraFailed:
                    resultClass = "INFRASTRUCTURE_ERROR";
                    return Result();

                default:
                    throw new InvalidOperationException($"Unsupported campaign state {campaign.State}.");
            }

            if (stopAfterState == campaign.State)
            {
                return Result(paused: true);
            }
        }

        if (lease is not null)
        {
            await backend.ReleaseAsync(lease, cancellationToken).ConfigureAwait(false);
        }

        return Result();

        CampaignRunResult Result(bool paused = false) => new(
            spec.CampaignId,
            spec.Scenario,
            campaign.State,
            capability,
            resultClass,
            artifact,
            analysis,
            signature,
            campaign.Events.ToArray(),
            paused);
    }

    private async Task<(ArtifactReceipt? Artifact, TriageAnalysis? Analysis, string? Signature, string ResultClass)> ObserveAsync(
        CampaignAggregate campaign,
        LabLease lease,
        CampaignSpec spec,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var resultClass = "INCOMPLETE";
        var failureId = ReadFailureId(campaign.Events.Count == 0 ? null : campaign.Events[^1].Reason);
        await foreach (var labEvent in backend.ObserveAsync(lease, cancellationToken).ConfigureAwait(false))
        {
            switch (labEvent.Kind)
            {
                case LabEventKind.CaseDone when campaign.State == CampaignState.Running:
                    await PersistTransitionAsync(campaign, CampaignState.CaseDone, "simulator reported case completion", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                    return (null, null, null, "COMPLETE");

                case LabEventKind.OperationTimeout:
                    resultClass = "OPERATION_TIMEOUT";
                    failureId = labEvent.FailureId;
                    await MarkSuspectedAsync(campaign, labEvent, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case LabEventKind.AgentLoss:
                    resultClass = "AGENT_LOSS";
                    failureId = labEvent.FailureId;
                    await MarkSuspectedAsync(campaign, labEvent, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case LabEventKind.BootIdChanged:
                    resultClass = "REBOOT";
                    failureId = labEvent.FailureId;
                    await MarkSuspectedAsync(campaign, labEvent, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case LabEventKind.FailureDetected:
                case LabEventKind.FlakySignature:
                    resultClass = "SYNTHETIC_FAILURE";
                    failureId = labEvent.FailureId;
                    await MarkSuspectedAsync(campaign, labEvent, correlationId, cancellationToken).ConfigureAwait(false);
                    break;

                case LabEventKind.InfrastructureFailure when !CampaignAggregate.IsTerminal(campaign.State):
                    await PersistTransitionAsync(campaign, CampaignState.InfraFailed, $"infrastructure:{labEvent.Detail ?? "unspecified"}", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                    return (null, null, null, "INFRASTRUCTURE_ERROR");

                case LabEventKind.ArtifactStable when campaign.State == CampaignState.SuspectedFailure:
                    {
                        failureId ??= labEvent.FailureId;
                        if (string.IsNullOrWhiteSpace(failureId))
                        {
                            await PersistTransitionAsync(campaign, CampaignState.Quarantined, "stable artifact has no failure correlation", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                            return (null, null, null, "QUARANTINED");
                        }

                        await PersistTransitionAsync(campaign, CampaignState.Collecting, "synthetic artifact reported stable", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                        var receipt = await backend.CollectAsync(lease, new FailureRef(failureId, spec.Scenario), cancellationToken).ConfigureAwait(false);
                        var actualHash = Convert.ToHexString(SHA256.HashData(receipt.Bytes)).ToLowerInvariant();
                        if (!receipt.IsStable || !receipt.IsSynthetic || !string.Equals(actualHash, receipt.Sha256, StringComparison.Ordinal))
                        {
                            await PersistTransitionAsync(campaign, CampaignState.Quarantined, "artifact integrity check failed", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                            return (receipt, null, null, "QUARANTINED");
                        }

                        await PersistTransitionAsync(campaign, CampaignState.Triaging, "synthetic artifact committed for fixture triage", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                        var parsed = SyntheticTriageParser.Parse(System.Text.Encoding.UTF8.GetString(receipt.Bytes));
                        var computed = SignatureV1.Compute(parsed);
                        await PersistTransitionAsync(campaign, CampaignState.Complete, "synthetic signature computed; no kernel claim", labEvent.VirtualTimestampMs, correlationId, cancellationToken).ConfigureAwait(false);
                        return (receipt, parsed, computed, resultClass);
                    }
            }
        }

        if (!CampaignAggregate.IsTerminal(campaign.State))
        {
            await PersistTransitionAsync(campaign, CampaignState.InfraFailed, "scenario ended before a terminal observation", CurrentElapsed(campaign), correlationId, cancellationToken).ConfigureAwait(false);
        }

        return (null, null, null, "INFRASTRUCTURE_ERROR");
    }

    private async Task MarkSuspectedAsync(
        CampaignAggregate campaign,
        LabEvent labEvent,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (campaign.State == CampaignState.Running)
        {
            await PersistTransitionAsync(
                campaign,
                CampaignState.SuspectedFailure,
                $"failure:{labEvent.FailureId ?? "uncorrelated"};kind:{labEvent.Kind}",
                labEvent.VirtualTimestampMs,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistTransitionAsync(
        CampaignAggregate campaign,
        CampaignState state,
        string reason,
        long virtualElapsedMs,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var campaignEvent = campaign.Transition(
            state,
            reason,
            "kcrash-controller",
            DeterministicEpoch.AddMilliseconds(virtualElapsedMs),
            virtualElapsedMs,
            correlationId);
        _ = await eventStore.AppendAsync(campaignEvent, cancellationToken).ConfigureAwait(false);
    }

    private static long CurrentElapsed(CampaignAggregate campaign) =>
        campaign.Events.Count == 0 ? 0 : campaign.Events[^1].VirtualElapsedMs;

    private static string? ReadFailureId(string? reason)
    {
        if (reason is null || !reason.StartsWith("failure:", StringComparison.Ordinal))
        {
            return null;
        }

        var separator = reason.IndexOf(';');
        return separator < 0 ? reason[8..] : reason[8..separator];
    }
}
