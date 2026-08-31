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

public static class SimulationEvidenceContract
{
    public const string BackendId = "SIMULATED_BACKEND_V1";
    public const string SimulatorVersion = "1.0.0";
    public const string VirtualEpochUtc = "2026-08-31T00:00:00.0000000+00:00";
    public const string ScenarioFixtureDigestAlgorithm = "SCENARIO_JSON_SHA256_V1";
}

public static class ScenarioFixtureIdentity
{
    public static string DefinitionDigest(ScenarioFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(fixture, ContractJson.Compact);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
