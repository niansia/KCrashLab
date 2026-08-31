using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Simulation;

public sealed class SimulatedLabBackend : ILabBackend
{
    public const string Version = SimulationEvidenceContract.SimulatorVersion;

    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse(
        SimulationEvidenceContract.VirtualEpochUtc,
        System.Globalization.CultureInfo.InvariantCulture);
    private readonly ScenarioFixture fixture;
    private readonly VirtualClock clock = new(Epoch);
    private CampaignSpec? activeSpec;
    private CanonicalCase? activeCase;

    public SimulatedLabBackend(ScenarioFixture fixture)
    {
        this.fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public int DispatchCount { get; private set; }

    public long VirtualElapsedMilliseconds => clock.ElapsedMilliseconds;

    public Task<CapabilityReport> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, string> evidence = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["backend_contract"] = SimulationEvidenceContract.BackendId,
            ["scenario_fixture_digest"] = ScenarioFixtureIdentity.DefinitionDigest(fixture),
            ["scenario_fixture_digest_algorithm"] = SimulationEvidenceContract.ScenarioFixtureDigestAlgorithm,
            ["scenario_fixture_name"] = fixture.Name,
            ["scenario_fixture_schema_version"] = fixture.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["simulator_version"] = Version,
            ["virtual_epoch_utc"] = SimulationEvidenceContract.VirtualEpochUtc
        };
        return Task.FromResult(new CapabilityReport(
            1,
            "SIMULATED",
            Epoch,
            new HostDescription("KCrashLab deterministic simulator", Version, "VIRTUAL", null),
            new CapabilitySet(
                CapabilityStatus.Blocked,
                CapabilityStatus.Blocked,
                CapabilityStatus.Blocked,
                CapabilityStatus.Blocked,
                CapabilityStatus.Blocked),
            CapabilityStatus.Blocked,
            ["Simulation semantics are independent of host kernel-lab capabilities."],
            evidence));
    }

    public Task<LabLease> AcquireAsync(CampaignSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(spec.Scenario, fixture.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Campaign scenario does not match the backend fixture.");
        }

        activeSpec = spec;
        var lease = new LabLease(
            DeterministicIdentity.CreateGuid("lab-lease", spec.CampaignId, fixture.Name, fixture.Seed),
            spec.CampaignId,
            "simulated",
            fixture.BootId);
        return Task.FromResult(lease);
    }

    public Task<DispatchReceipt> DispatchAsync(LabLease lease, CanonicalCase testCase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLease(lease);
        activeCase = testCase;
        DispatchCount++;
        return Task.FromResult(new DispatchReceipt(
            DeterministicIdentity.CreateGuid("dispatch", lease.CampaignId, testCase.CaseId),
            DeterministicIdentity.CreateGuid("run", lease.CampaignId, testCase.CaseId, fixture.Name),
            testCase.CaseId,
            Duplicate: DispatchCount > 1));
    }

    public async IAsyncEnumerable<LabEvent> ObserveAsync(
        LabLease lease,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureLease(lease);
        var index = 0;
        foreach (var item in fixture.Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.AdvanceTo(item.AtMs);
            var kind = ScenarioFixtureLoader.ParseEventKind(item.Kind);
            yield return new LabEvent(
                DeterministicIdentity.CreateGuid("lab-event", lease.CampaignId, fixture.Name, index, kind, item.AtMs),
                kind,
                item.AtMs,
                fixture.BootId,
                item.Detail,
                item.FailureId,
                item.ExpectedSha256,
                item.ArtifactLength);
            index++;
            await Task.Yield();
        }
    }

    public Task<ArtifactReceipt> CollectAsync(LabLease lease, FailureRef failure, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLease(lease);
        var artifact = fixture.Artifact ?? throw new InvalidOperationException("Scenario has no collectable artifact.");
        var bytes = Encoding.UTF8.GetBytes(artifact.Utf8Content);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, artifact.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture artifact hash changed after validation.");
        }

        var reported = artifact.CorruptHash ? new string('0', 64) : artifact.Sha256;
        if (string.Equals(actual, reported, StringComparison.Ordinal) && failure.FailureId.Length == 0)
        {
            throw new InvalidDataException("Failure reference is empty.");
        }

        return Task.FromResult(new ArtifactReceipt(
            KCrashLab.StorageNameRules.Normalize(artifact.RelativeName),
            bytes,
            reported,
            IsStable: true,
            IsSynthetic: true));
    }

    public Task ResetAsync(LabLease lease, ResetPolicy policy, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLease(lease);
        if (policy != ResetPolicy.SimulatedClean)
        {
            throw new InvalidOperationException("Simulator only supports simulated clean reset.");
        }

        clock.Reset();
        activeCase = null;
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(LabLease lease, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLease(lease);
        activeSpec = null;
        activeCase = null;
        return Task.CompletedTask;
    }

    private void EnsureLease(LabLease lease)
    {
        if (activeSpec is null || activeSpec.CampaignId != lease.CampaignId || lease.Backend != "simulated")
        {
            throw new InvalidOperationException("Lease is not active in this backend instance.");
        }
    }
}
