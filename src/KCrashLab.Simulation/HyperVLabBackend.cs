using System.Runtime.CompilerServices;
using KCrashLab.Contracts;

namespace KCrashLab.Simulation;

public sealed class HyperVLabBackend : ILabBackend
{
    private const string BlockedMessage = "Hyper-V kernel-lab execution is BLOCKED_BY_ENVIRONMENT in KCrashLab v1.";
    public Task<CapabilityReport> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HostCapabilityProbe.Probe(DateTimeOffset.UtcNow));
    }

    public Task<LabLease> AcquireAsync(CampaignSpec spec, CancellationToken cancellationToken) =>
        Task.FromException<LabLease>(new BackendBlockedException(BlockedMessage));

    public Task<DispatchReceipt> DispatchAsync(LabLease lease, CanonicalCase testCase, CancellationToken cancellationToken) =>
        Task.FromException<DispatchReceipt>(new BackendBlockedException(BlockedMessage));

    public async IAsyncEnumerable<LabEvent> ObserveAsync(LabLease lease, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new BackendBlockedException(BlockedMessage);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public Task<ArtifactReceipt> CollectAsync(LabLease lease, FailureRef failure, CancellationToken cancellationToken) =>
        Task.FromException<ArtifactReceipt>(new BackendBlockedException(BlockedMessage));

    public Task ResetAsync(LabLease lease, ResetPolicy policy, CancellationToken cancellationToken) =>
        Task.FromException(new BackendBlockedException(BlockedMessage));

    public Task ReleaseAsync(LabLease lease, CancellationToken cancellationToken) =>
        Task.FromException(new BackendBlockedException(BlockedMessage));
}
