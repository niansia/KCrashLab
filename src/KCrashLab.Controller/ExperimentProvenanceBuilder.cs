using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;

namespace KCrashLab.Controller;

public static class ExperimentProvenanceBuilder
{
    public const string EngineVersion = "1.3.0-sim";
    public const string Unspecified = "UNSPECIFIED";
    public const string Uncommitted = "UNCOMMITTED";

    private static readonly string[] SourceDirectories =
    [
        ".github", "docs", "drivers", "samples", "schemas", "scripts", "src", "tests"
    ];

    private static readonly string[] SourceFiles =
    [
        ".editorconfig", ".gitattributes", ".gitignore", "Directory.Build.props", "Directory.Packages.props",
        "global.json", "KCrashLab.sln", "README.md", "SECURITY.md"
    ];

    public static Task<ExperimentProvenance> ForFuzzAsync(
        string repositoryRoot,
        FuzzCampaignResult result,
        string recordedAtUtc,
        string gitCommit,
        CancellationToken cancellationToken) =>
        CreateAsync(
            repositoryRoot,
            recordedAtUtc,
            gitCommit,
            result.Corpus[0].TestCase.Value.SchemaVersion,
            FuzzDefinition(result),
            cancellationToken);

    public static Task<ExperimentProvenance> ForE1Async(
        string repositoryRoot,
        E1ExperimentResult result,
        string recordedAtUtc,
        string gitCommit,
        CancellationToken cancellationToken) =>
        CreateAsync(
            repositoryRoot,
            recordedAtUtc,
            gitCommit,
            caseSchemaVersion: 1,
            E1Definition(result),
            cancellationToken);

    public static Task<ExperimentProvenance> ForE2Async(
        string repositoryRoot,
        E2ExperimentResult result,
        string recordedAtUtc,
        string gitCommit,
        CancellationToken cancellationToken) =>
        CreateAsync(
            repositoryRoot,
            recordedAtUtc,
            gitCommit,
            caseSchemaVersion: 1,
            E2Definition(result),
            cancellationToken);

    public static ExperimentProvenance UnspecifiedForFuzz(FuzzCampaignResult result) =>
        new(
            Unspecified,
            Uncommitted,
            Unspecified,
            DefinitionDigest(FuzzDefinition(result)),
            result.Corpus[0].TestCase.Value.SchemaVersion,
            EngineVersion);

    public static ExperimentProvenance UnspecifiedForE1(E1ExperimentResult result) =>
        new(
            Unspecified,
            Uncommitted,
            Unspecified,
            DefinitionDigest(E1Definition(result)),
            1,
            EngineVersion);

    public static ExperimentProvenance UnspecifiedForE2(E2ExperimentResult result) =>
        new(
            Unspecified,
            Uncommitted,
            Unspecified,
            DefinitionDigest(E2Definition(result)),
            1,
            EngineVersion);

    public static string FuzzDefinitionDigest(
        string executionMode,
        string strategy,
        long campaignSeed,
        int budget,
        string seedCaseId,
        int caseSchemaVersion) =>
        DefinitionDigest(new
        {
            experiment = "G3_FUZZ_DISCOVERY_V1",
            execution_mode = executionMode,
            strategy,
            campaign_seed = campaignSeed,
            budget,
            seed_case_id = seedCaseId,
            case_schema_version = caseSchemaVersion,
            engine_version = EngineVersion
        });

    public static string E1DefinitionDigest(
        string experiment,
        string executionMode,
        string seedCaseId,
        int budgetPerTrial,
        int trialsPerStrategy,
        long baseCampaignSeed,
        int caseSchemaVersion) =>
        DefinitionDigest(new
        {
            experiment,
            execution_mode = executionMode,
            seed_case_id = seedCaseId,
            budget_per_trial = budgetPerTrial,
            trials_per_strategy = trialsPerStrategy,
            base_campaign_seed = baseCampaignSeed,
            case_schema_version = caseSchemaVersion,
            engine_version = EngineVersion
        });

    public static string E2DefinitionDigest(
        string experiment,
        string executionMode,
        string seedCaseId,
        int budgetPerTrial,
        int trialsPerMode,
        long baseCampaignSeed,
        int singleCallMaximumSequenceLength,
        int statefulMaximumSequenceLength,
        int caseSchemaVersion) =>
        DefinitionDigest(new
        {
            experiment,
            execution_mode = executionMode,
            seed_case_id = seedCaseId,
            budget_per_trial = budgetPerTrial,
            trials_per_mode = trialsPerMode,
            base_campaign_seed = baseCampaignSeed,
            single_call_maximum_sequence_length = singleCallMaximumSequenceLength,
            stateful_maximum_sequence_length = statefulMaximumSequenceLength,
            case_schema_version = caseSchemaVersion,
            engine_version = EngineVersion
        });

    public static ExperimentProvenance ParseAndValidate(JsonElement element)
    {
        var provenance = new ExperimentProvenance(
            element.GetProperty("recorded_at_utc").GetString() ?? throw new InvalidDataException("recorded_at_utc is null."),
            element.GetProperty("git_commit").GetString() ?? throw new InvalidDataException("git_commit is null."),
            element.GetProperty("source_tree_digest").GetString() ?? throw new InvalidDataException("source_tree_digest is null."),
            element.GetProperty("experiment_definition_digest").GetString() ?? throw new InvalidDataException("experiment_definition_digest is null."),
            element.GetProperty("case_schema_version").GetInt32(),
            element.GetProperty("engine_version").GetString() ?? throw new InvalidDataException("engine_version is null."));
        Validate(provenance);
        return provenance;
    }

    public static void Validate(ExperimentProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ValidateRecordedAt(provenance.RecordedAtUtc);
        ValidateGitCommit(provenance.GitCommit);
        ValidateDigestOrUnspecified(provenance.SourceTreeDigest, "source_tree_digest");
        ValidateDigest(provenance.ExperimentDefinitionDigest, "experiment_definition_digest");
        if (provenance.CaseSchemaVersion != 1 || provenance.EngineVersion != EngineVersion)
        {
            throw new InvalidDataException("Provenance contains an unsupported Case IR or engine version.");
        }
    }

    private static async Task<ExperimentProvenance> CreateAsync(
        string repositoryRoot,
        string recordedAtUtc,
        string gitCommit,
        int caseSchemaVersion,
        object definition,
        CancellationToken cancellationToken)
    {
        ValidateRecordedAt(recordedAtUtc);
        ValidateGitCommit(gitCommit);
        return new ExperimentProvenance(
            recordedAtUtc,
            gitCommit,
            await SourceTreeDigestAsync(repositoryRoot, cancellationToken).ConfigureAwait(false),
            DefinitionDigest(definition),
            caseSchemaVersion,
            EngineVersion);
    }

    private static async Task<string> SourceTreeDigestAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var files = new List<string>();
        foreach (var directoryName in SourceDirectories)
        {
            var directory = Path.Combine(root, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(static path => !HasIgnoredSegment(path)));
        }

        files.AddRange(SourceFiles
            .Select(name => Path.Combine(root, name))
            .Where(File.Exists));
        var ordered = files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = Path.GetRelativePath(root, path).Replace('\\', '/')
            })
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0 || ordered.Length > 10_000)
        {
            throw new InvalidDataException("Source tree file count is outside the provenance limit.");
        }

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathBytes = Encoding.UTF8.GetBytes(file.RelativePath + "\0");
            aggregate.AppendData(pathBytes);
            await using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fileHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            aggregate.AppendData(fileHash);
        }

        return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool HasIgnoredSegment(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment => segment is "bin" or "obj" or ".git" or "artifacts" or "results" or "TestResults");
    }

    private static string DefinitionDigest(object definition) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition, ContractJson.Compact))).ToLowerInvariant();

    private static object FuzzDefinition(FuzzCampaignResult result) => new
    {
        experiment = "G3_FUZZ_DISCOVERY_V1",
        execution_mode = result.ExecutionMode,
        strategy = result.Strategy,
        campaign_seed = result.CampaignSeed,
        budget = result.Budget,
        seed_case_id = result.SeedCaseId,
        case_schema_version = result.Corpus[0].TestCase.Value.SchemaVersion,
        engine_version = EngineVersion
    };

    private static object E1Definition(E1ExperimentResult result) => new
    {
        experiment = result.Experiment,
        execution_mode = result.ExecutionMode,
        seed_case_id = result.SeedCaseId,
        budget_per_trial = result.BudgetPerTrial,
        trials_per_strategy = result.TrialsPerStrategy,
        base_campaign_seed = result.BaseCampaignSeed,
        case_schema_version = 1,
        engine_version = EngineVersion
    };

    private static object E2Definition(E2ExperimentResult result) => new
    {
        experiment = result.Experiment,
        execution_mode = result.ExecutionMode,
        seed_case_id = result.SeedCaseId,
        budget_per_trial = result.BudgetPerTrial,
        trials_per_mode = result.TrialsPerMode,
        base_campaign_seed = result.BaseCampaignSeed,
        single_call_maximum_sequence_length = result.SingleCallMaximumSequenceLength,
        stateful_maximum_sequence_length = result.StatefulMaximumSequenceLength,
        case_schema_version = 1,
        engine_version = EngineVersion
    };

    private static void ValidateRecordedAt(string value)
    {
        if (value != Unspecified
            && (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                || parsed.Offset != TimeSpan.Zero))
        {
            throw new InvalidDataException("recorded_at_utc must be UNSPECIFIED or an ISO-8601 UTC timestamp.");
        }
    }

    private static void ValidateGitCommit(string value)
    {
        if (value == Uncommitted)
        {
            return;
        }

        if (value.Length is not (40 or 64) || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("git_commit must be UNCOMMITTED or a 40/64-character hexadecimal object ID.");
        }
    }

    private static void ValidateDigestOrUnspecified(string value, string name)
    {
        if (value != Unspecified)
        {
            ValidateDigest(value, name);
        }
    }

    private static void ValidateDigest(string value, string name)
    {
        if (value.Length != 64 || value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{name} must be a SHA-256 digest.");
        }
    }
}
