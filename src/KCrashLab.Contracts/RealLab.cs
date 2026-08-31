namespace KCrashLab.Contracts;

public sealed record RealLabProfile(
    int SchemaVersion,
    string ExecutionMode,
    string VmId,
    string CheckpointId,
    string GuestBuild,
    string DriverSha256,
    string DriverFileName,
    string DevicePath,
    string DeviceInterfaceGuid,
    string ExchangeRoot,
    string DumpRelativePath,
    string AuthorizationId,
    bool DisposableLab,
    bool NetworkIsolated,
    bool ImmutableCheckpoint,
    int HeartbeatTimeoutSeconds,
    int DumpStabilitySeconds);

public sealed record RealLabValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed record RealLabAcquisitionReplay(
    int Attempt,
    string Classification,
    string? Signature,
    string DumpSha256,
    string JournalSha256,
    string WatchdogSha256);

public sealed record RealLabAcquisition(
    int SchemaVersion,
    string ExecutionMode,
    string OriginalCasePath,
    string MinimizedCasePath,
    string DriverSha256,
    string VmIdentitySha256,
    string CheckpointIdentitySha256,
    string AuthorizationSha256,
    string GuestBuild,
    string SymbolsSha256,
    string DumpSha256,
    string RawWindbgSha256,
    string DiscoveryJournalSha256,
    string DiscoveryWatchdogSha256,
    string RawWindbgPath,
    IReadOnlyList<RealLabAcquisitionReplay> Replays,
    DateTimeOffset RecordedAtUtc,
    string GitCommit);
