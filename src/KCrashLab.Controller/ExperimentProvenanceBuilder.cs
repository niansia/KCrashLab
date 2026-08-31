using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Controller;

public static class ExperimentProvenanceBuilder
{
    public const string EngineVersion = "1.4.0-sim";
    public const string Unspecified = "UNSPECIFIED";
    public const string Uncommitted = "UNCOMMITTED";
    public const string SourceCommitTime = "SOURCE_COMMIT_TIME";

    private const string DecisionStream = "SHA256_LANES_V1";
    private const string TerminationRule = "BUDGET_OR_SCHEDULER_LIMIT_V1";
    private const string EnergyRankingRule = "GREEDY_ENERGY_DIV_SELECTIONS_PLUS_ONE_THEN_LAST_SELECTED_THEN_CASE_ID_V1";
    private const string PairedSeedRule = "BASE_PLUS_TRIAL_MINUS_ONE_V1";

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
            Unspecified,
            Unspecified,
            Uncommitted,
            Unspecified,
            DefinitionDigest(FuzzDefinition(result)),
            result.Corpus[0].TestCase.Value.SchemaVersion,
            EngineVersion);

    public static ExperimentProvenance UnspecifiedForE1(E1ExperimentResult result) =>
        new(
            Unspecified,
            Unspecified,
            Unspecified,
            Uncommitted,
            Unspecified,
            DefinitionDigest(E1Definition(result)),
            1,
            EngineVersion);

    public static ExperimentProvenance UnspecifiedForE2(E2ExperimentResult result) =>
        new(
            Unspecified,
            Unspecified,
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
        DefinitionDigest(FuzzDefinitionCore(
            executionMode,
            strategy,
            campaignSeed,
            budget,
            seedCaseId,
            MutationCandidateSampling.AlgorithmId,
            MutationCandidateSampling.DefaultMaximumCandidatesPerOperator,
            caseSchemaVersion));

    public static string E1DefinitionDigest(
        string experiment,
        string executionMode,
        string seedCaseId,
        int budgetPerTrial,
        int trialsPerStrategy,
        long baseCampaignSeed,
        int caseSchemaVersion) =>
        DefinitionDigest(E1DefinitionCore(
            experiment,
            executionMode,
            seedCaseId,
            budgetPerTrial,
            trialsPerStrategy,
            baseCampaignSeed,
            caseSchemaVersion));

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
        DefinitionDigest(E2DefinitionCore(
            experiment,
            executionMode,
            seedCaseId,
            budgetPerTrial,
            trialsPerMode,
            baseCampaignSeed,
            singleCallMaximumSequenceLength,
            statefulMaximumSequenceLength,
            caseSchemaVersion));

    public static ExperimentProvenance ParseAndValidate(JsonElement element)
    {
        var provenance = new ExperimentProvenance(
            element.GetProperty("recorded_at_utc").GetString() ?? throw new InvalidDataException("recorded_at_utc is null."),
            element.TryGetProperty("source_commit_time_utc", out var commitTime)
                ? commitTime.GetString() ?? throw new InvalidDataException("source_commit_time_utc is null.")
                : Unspecified,
            element.TryGetProperty("reproducible_timestamp_policy", out var timestampPolicy)
                ? timestampPolicy.GetString() ?? throw new InvalidDataException("reproducible_timestamp_policy is null.")
                : Unspecified,
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
        ValidateRecordedAt(provenance.SourceCommitTimeUtc);
        if (provenance.ReproducibleTimestampPolicy is not (Unspecified or SourceCommitTime or "WALL_CLOCK"))
            throw new InvalidDataException("Unsupported reproducible_timestamp_policy.");
        if (provenance.RecordedAtUtc != Unspecified && provenance.SourceCommitTimeUtc != Unspecified
            && DateTimeOffset.Parse(provenance.RecordedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                < DateTimeOffset.Parse(provenance.SourceCommitTimeUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            throw new InvalidDataException("recorded_at_utc cannot precede source_commit_time_utc.");
        if (provenance.ReproducibleTimestampPolicy == SourceCommitTime
            && provenance.RecordedAtUtc != provenance.SourceCommitTimeUtc)
            throw new InvalidDataException("SOURCE_COMMIT_TIME policy requires equal artifact and commit timestamps.");
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
        ValidateGitCommit(gitCommit);
        var sourceCommitTime = gitCommit == Uncommitted
            ? Unspecified
            : await ReadCommitTimeAsync(repositoryRoot, gitCommit, cancellationToken).ConfigureAwait(false);
        var timestampPolicy = recordedAtUtc == SourceCommitTime ? SourceCommitTime
            : recordedAtUtc == Unspecified ? Unspecified : "WALL_CLOCK";
        var artifactTime = recordedAtUtc == SourceCommitTime ? sourceCommitTime : recordedAtUtc;
        var provenance = new ExperimentProvenance(
            artifactTime,
            sourceCommitTime,
            timestampPolicy,
            gitCommit,
            await SourceTreeDigestAsync(repositoryRoot, cancellationToken).ConfigureAwait(false),
            DefinitionDigest(definition),
            caseSchemaVersion,
            EngineVersion);
        Validate(provenance);
        return provenance;
    }

    private static async Task<string> ReadCommitTimeAsync(string repositoryRoot, string gitCommit, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git", $"show -s --format=%cI {gitCommit}")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start git for provenance.");
        var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0 || !DateTimeOffset.TryParse(output, out var parsed))
            throw new InvalidDataException("Unable to resolve source commit timestamp.");
        return parsed.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<string> SourceTreeDigestAsync(string repositoryRoot, CancellationToken cancellationToken)
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

    private static object FuzzDefinition(FuzzCampaignResult result) => FuzzDefinitionCore(
        result.ExecutionMode,
        result.Strategy,
        result.CampaignSeed,
        result.Budget,
        result.SeedCaseId,
        result.CandidateEnumeration,
        result.MaximumCandidatesPerOperator,
        result.Corpus[0].TestCase.Value.SchemaVersion);

    private static object E1Definition(E1ExperimentResult result) => E1DefinitionCore(
        result.Experiment,
        result.ExecutionMode,
        result.SeedCaseId,
        result.BudgetPerTrial,
        result.TrialsPerStrategy,
        result.BaseCampaignSeed,
        caseSchemaVersion: 1);

    private static object E2Definition(E2ExperimentResult result) => E2DefinitionCore(
        result.Experiment,
        result.ExecutionMode,
        result.SeedCaseId,
        result.BudgetPerTrial,
        result.TrialsPerMode,
        result.BaseCampaignSeed,
        result.SingleCallMaximumSequenceLength,
        result.StatefulMaximumSequenceLength,
        caseSchemaVersion: 1);

    private static object FuzzDefinitionCore(
        string executionMode,
        string strategy,
        long campaignSeed,
        int budget,
        string seedCaseId,
        string candidateEnumeration,
        int maximumCandidatesPerOperator,
        int caseSchemaVersion) => new
        {
            experiment = "G3_FUZZ_DISCOVERY_V1",
            execution_mode = executionMode,
            strategy,
            campaign_seed = campaignSeed,
            budget,
            seed_case_id = seedCaseId,
            mutation_operators = MutationOperatorIds(),
            candidate_enumeration = candidateEnumeration,
            maximum_candidates_per_operator = maximumCandidatesPerOperator,
            decision_stream = DecisionStream,
            termination_rule = TerminationRule,
            case_schema_version = caseSchemaVersion,
            engine_version = EngineVersion
        };

    private static object E1DefinitionCore(
        string experiment,
        string executionMode,
        string seedCaseId,
        int budgetPerTrial,
        int trialsPerStrategy,
        long baseCampaignSeed,
        int caseSchemaVersion) => new
        {
            experiment,
            execution_mode = executionMode,
            target = "kcl.state",
            seed_case_id = seedCaseId,
            budget_per_trial = budgetPerTrial,
            trials_per_strategy = trialsPerStrategy,
            base_campaign_seed = baseCampaignSeed,
            corpus_admission_policies = new[] { "KEEP_ALL", "NOVELTY_ONLY" },
            parent_selection_policies = new[] { "UNIFORM", "ENERGY_RANKED_V1" },
            operator_selection_policy = "UNIFORM",
            candidate_selection_policy = "UNIFORM",
            energy_ranking_rule = EnergyRankingRule,
            strategy_ids = new[]
            {
                E1ExperimentRunner.KeepAllUniform,
                E1ExperimentRunner.KeepAllEnergyRanked,
                E1ExperimentRunner.NoveltyUniform,
                E1ExperimentRunner.NoveltyEnergyRanked
            },
            mutation_operators = MutationOperatorIds(),
            candidate_enumeration = MutationCandidateSampling.AlgorithmId,
            maximum_candidates_per_operator = MutationCandidateSampling.DefaultMaximumCandidatesPerOperator,
            decision_stream = DecisionStream,
            paired_seed_rule = PairedSeedRule,
            termination_rule = TerminationRule,
            case_schema_version = caseSchemaVersion,
            engine_version = EngineVersion
        };

    private static object E2DefinitionCore(
        string experiment,
        string executionMode,
        string seedCaseId,
        int budgetPerTrial,
        int trialsPerMode,
        long baseCampaignSeed,
        int singleCallMaximumSequenceLength,
        int statefulMaximumSequenceLength,
        int caseSchemaVersion) => new
        {
            experiment,
            execution_mode = executionMode,
            target = "kcl.state",
            seed_case_id = seedCaseId,
            budget_per_trial = budgetPerTrial,
            trials_per_mode = trialsPerMode,
            base_campaign_seed = baseCampaignSeed,
            modes = new[] { "SINGLE_CALL", "STATEFUL" },
            single_call_maximum_sequence_length = singleCallMaximumSequenceLength,
            stateful_maximum_sequence_length = statefulMaximumSequenceLength,
            corpus_admission_policy = "NOVELTY_ONLY",
            parent_selection_policy = "ENERGY_RANKED_V1",
            operator_selection_policy = "UNIFORM",
            candidate_selection_policy = "UNIFORM",
            energy_ranking_rule = EnergyRankingRule,
            mutation_operators = MutationOperatorIds(),
            candidate_enumeration = MutationCandidateSampling.AlgorithmId,
            maximum_candidates_per_operator = MutationCandidateSampling.DefaultMaximumCandidatesPerOperator,
            decision_stream = DecisionStream,
            sequence_cap_rule = "REJECT_CANDIDATE_ABOVE_MODE_MAXIMUM_V1",
            paired_seed_rule = PairedSeedRule,
            termination_rule = TerminationRule,
            case_schema_version = caseSchemaVersion,
            engine_version = EngineVersion
        };

    private static string[] MutationOperatorIds() =>
        DefaultMutationOperators.Create().Select(static item => item.OperatorId).ToArray();

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
