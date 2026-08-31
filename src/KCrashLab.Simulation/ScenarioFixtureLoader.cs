using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using KCrashLab.Contracts;

namespace KCrashLab.Simulation;

public static class ScenarioFixtureLoader
{
    public static async Task<ScenarioFixture> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists)
        {
            throw new FileNotFoundException("Scenario fixture was not found.", info.FullName);
        }

        if (info.Length > 1_048_576)
        {
            throw new InvalidDataException("Scenario fixture is too large.");
        }

        await using var stream = info.OpenRead();
        var fixture = await JsonSerializer.DeserializeAsync<ScenarioFixture>(stream, ContractJson.Compact, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Scenario fixture is empty.");
        Validate(fixture);
        return fixture;
    }

    private static void Validate(ScenarioFixture fixture)
    {
        if (fixture.SchemaVersion != 1 || fixture.ExecutionMode != "SIMULATED")
        {
            throw new InvalidDataException("Scenario must use schema v1 and execution_mode SIMULATED.");
        }

        if (fixture.Events.Count is < 1 or > 128)
        {
            throw new InvalidDataException("Scenario event count is outside the allowed range.");
        }

        long previous = -1;
        foreach (var scenarioEvent in fixture.Events)
        {
            if (scenarioEvent.AtMs < previous)
            {
                throw new InvalidDataException("Scenario virtual timestamps must be monotonic.");
            }

            previous = scenarioEvent.AtMs;
            _ = ParseEventKind(scenarioEvent.Kind);
        }

        if (fixture.Artifact is { } artifact)
        {
            if (artifact.Utf8Content.Length > 1_048_576)
            {
                throw new InvalidDataException("Scenario artifact is too large.");
            }

            _ = KCrashLab.StorageNameRules.Normalize(artifact.RelativeName);
            var bytes = Encoding.UTF8.GetBytes(artifact.Utf8Content);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(actualHash, artifact.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Scenario artifact sha256 does not match its UTF-8 content.");
            }

            var stable = fixture.Events.LastOrDefault(static item =>
                string.Equals(item.Kind, "ARTIFACT_STABLE", StringComparison.Ordinal));
            if (stable?.ArtifactLength != bytes.Length)
            {
                throw new InvalidDataException("ARTIFACT_STABLE length does not match fixture content.");
            }
        }
    }

    internal static LabEventKind ParseEventKind(string value)
    {
        var compact = value.Replace("_", string.Empty, StringComparison.Ordinal);
        if (!Enum.TryParse<LabEventKind>(compact, ignoreCase: true, out var kind))
        {
            throw new InvalidDataException($"Unknown scenario event kind '{value}'.");
        }

        return kind;
    }
}
