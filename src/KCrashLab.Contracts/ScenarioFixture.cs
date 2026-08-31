namespace KCrashLab.Contracts;

public sealed record ScenarioEventFixture(
    string Kind,
    long AtMs,
    string? Detail,
    string? FailureId,
    long? ArtifactLength,
    string? ExpectedSha256);

public sealed record ScenarioArtifactFixture(
    string RelativeName,
    string Utf8Content,
    string Sha256,
    bool CorruptHash);

public sealed record ScenarioFixture(
    int SchemaVersion,
    string Name,
    string ExecutionMode,
    string BootId,
    long Seed,
    IReadOnlyList<ScenarioEventFixture> Events,
    ScenarioArtifactFixture? Artifact,
    string? ExpectedResultClass);
