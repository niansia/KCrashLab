using System.Security.Cryptography;
using KCrashLab.Contracts;
using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class FaultInjectionTests
{
    [Fact]
    public async Task SqliteDuplicateCollisionAndSequenceFailureRemainAtomic()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var store = new SqliteCampaignEventStore(Path.Combine(temporary, "events.db"));
            await store.InitializeAsync(CancellationToken.None);
            var campaignId = Guid.Parse("47caf626-9a7f-4ccf-bc26-d49d677cf22b");
            var first = Event(campaignId, 1, Guid.Parse("13cb9a69-d30a-446f-8b12-a482b8c115af"), "first");

            Assert.True(await store.AppendAsync(first, CancellationToken.None));
            Assert.False(await store.AppendAsync(first, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.AppendAsync(first with { Reason = "collision" }, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(
                Event(campaignId, 3, Guid.Parse("73db030d-537b-4b9d-a073-755d43916bbc"), "gap"),
                CancellationToken.None));

            var stored = await store.ReadAsync(campaignId, CancellationToken.None);
            Assert.Single(stored);
            Assert.Equal(first, stored[0]);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public async Task AmbiguousCommitAfterDispatchResumesWithoutRedispatch()
    {
        var fixture = await ScenarioFixtureLoader.LoadAsync(TestPaths.Sample("scenarios", "dump-ready.json"), CancellationToken.None);
        var testCase = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));
        var campaignId = DeterministicIdentity.CreateGuid("ambiguous-commit", testCase.CaseId);
        var store = new AmbiguousCommitEventStore(failAfterPersistingSequence: 5);
        var firstBackend = new SimulatedLabBackend(fixture);

        await Assert.ThrowsAsync<IOException>(() => new CampaignOrchestrator(store, firstBackend).RunAsync(
            new CampaignSpec(campaignId, fixture.Name, fixture.Seed),
            testCase,
            null,
            CancellationToken.None));
        Assert.Equal(1, firstBackend.DispatchCount);

        store.DisableFailure();
        var resumedBackend = new SimulatedLabBackend(fixture);
        var resumed = await new CampaignOrchestrator(store, resumedBackend).RunAsync(
            new CampaignSpec(campaignId, fixture.Name, fixture.Seed),
            testCase,
            null,
            CancellationToken.None);
        Assert.Equal(CampaignState.Complete, resumed.State);
        Assert.Equal(0, resumedBackend.DispatchCount);
        Assert.Equal(Enumerable.Range(1, resumed.Events.Count), resumed.Events.Select(static item => (int)item.Sequence));
    }

    [Fact]
    public async Task CasInterruptedWriteRemovesStagingAndCorruptExistingBlobFailsClosed()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var store = new ContentAddressedStore(temporary);
            await using var failing = new ThrowingReadStream([1, 2, 3, 4]);
            await Assert.ThrowsAsync<IOException>(() => store.PutAsync(failing, CancellationToken.None));
            Assert.Empty(Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories));

            var expected = "trusted content"u8.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
            var directory = Path.Combine(temporary, hash[..2]);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, hash), "corrupt");
            await using var source = new MemoryStream(expected, writable: false);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.PutAsync(source, CancellationToken.None));
            Assert.DoesNotContain(Directory.EnumerateFiles(temporary, "*.tmp", SearchOption.AllDirectories), static _ => true);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public async Task ManifestTruncationAndMissingArtifactAreRejected()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var evidence = Path.Combine(temporary, "evidence");
            Directory.CreateDirectory(evidence);
            var artifact = Path.Combine(evidence, "artifact.bin");
            await File.WriteAllTextAsync(artifact, "evidence");
            await EvidenceManifest.CreateAsync(evidence, CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(evidence, EvidenceManifest.FileName), "truncated\n");
            var truncated = await EvidenceManifest.VerifyAsync(evidence, CancellationToken.None);
            Assert.False(truncated.IsValid);
            Assert.Contains(truncated.Errors, static error => error.Contains("Malformed manifest", StringComparison.Ordinal));

            await EvidenceManifest.CreateAsync(evidence, CancellationToken.None);
            File.Delete(artifact);
            var missing = await EvidenceManifest.VerifyAsync(evidence, CancellationToken.None);
            Assert.False(missing.IsValid);
            Assert.Contains(missing.Errors, static error => error.Contains("Cannot verify", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void VirtualClockRejectsBackwardNegativeAndOverflowJumpsWithoutStateChange()
    {
        var clock = new VirtualClock(DateTimeOffset.Parse("2026-08-31T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        clock.AdvanceTo(100);

        Assert.Throws<InvalidOperationException>(() => clock.AdvanceTo(99));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(long.MaxValue));
        Assert.Equal(100, clock.ElapsedMilliseconds);
    }

    private static CampaignEvent Event(Guid campaignId, long sequence, Guid eventId, string reason) => new(
        1,
        eventId,
        campaignId,
        sequence,
        sequence == 1 ? CampaignState.Created : CampaignState.Validating,
        CampaignState.Validating,
        reason,
        "test",
        DateTimeOffset.Parse("2026-08-31T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        0,
        Guid.Parse("24a2cd7a-62cc-4307-af25-90443373685c"));

    private sealed class AmbiguousCommitEventStore(long failAfterPersistingSequence) : ICampaignEventStore
    {
        private readonly List<CampaignEvent> events = [];
        private bool failureEnabled = true;

        public Task<IReadOnlyList<CampaignEvent>> ReadAsync(Guid campaignId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CampaignEvent>>(events.Where(item => item.CampaignId == campaignId).OrderBy(static item => item.Sequence).ToArray());
        }

        public Task<bool> AppendAsync(CampaignEvent campaignEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = events.SingleOrDefault(item => item.EventId == campaignEvent.EventId);
            if (existing is not null)
            {
                if (existing != campaignEvent)
                {
                    throw new InvalidDataException("Injected event ID collision.");
                }

                return Task.FromResult(false);
            }

            events.Add(campaignEvent);
            if (failureEnabled && campaignEvent.Sequence == failAfterPersistingSequence)
            {
                throw new IOException("Injected acknowledgement loss after durable append.");
            }

            return Task.FromResult(true);
        }

        public void DisableFailure() => failureEnabled = false;
    }

    private sealed class ThrowingReadStream(byte[] firstChunk) : Stream
    {
        private bool emitted;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (emitted)
            {
                return ValueTask.FromException<int>(new IOException("Injected source interruption."));
            }

            emitted = true;
            firstChunk.CopyTo(buffer);
            return ValueTask.FromResult(firstChunk.Length);
        }
    }
}
