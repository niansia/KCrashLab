using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KCrashLab.Contracts;
using KCrashLab.Storage;

namespace KCrashLab.Controller;

public sealed record E2ArtifactBuildResult(string Root, int ManifestEntries);

public sealed record E2ArtifactVerificationReport(bool IsValid, int VerifiedFiles, IReadOnlyList<string> Errors);

public static class E2ExperimentArtifacts
{
    private const string SimulationBanner = "SIMULATED — NOT A REAL KERNEL CRASH BENCHMARK";
    private static readonly JsonSerializerOptions ArtifactJson = CreateArtifactJson();

    public static Task<E2ArtifactBuildResult> BuildAsync(
        string outputRoot,
        E2ExperimentResult result,
        CancellationToken cancellationToken) =>
        BuildAsync(outputRoot, result, ExperimentProvenanceBuilder.UnspecifiedForE2(result), cancellationToken);

    public static async Task<E2ArtifactBuildResult> BuildAsync(
        string outputRoot,
        E2ExperimentResult result,
        ExperimentProvenance provenance,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(result);
        ExperimentProvenanceBuilder.Validate(provenance);
        if (result.SchemaVersion != 1 || result.ExecutionMode != "SIMULATED" || result.Experiment != "E2_STATEFUL_VS_SINGLE_CALL_V1")
        {
            throw new InvalidDataException("Only simulated E2 experiment v1 results can be written.");
        }

        var root = Path.GetFullPath(outputRoot);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidDataException("E2 experiment output directory must be empty.");
        }

        Directory.CreateDirectory(root);
        await WriteJsonAsync(Path.Combine(root, "summary.json"), new
        {
            schema_version = result.SchemaVersion,
            execution_mode = result.ExecutionMode,
            experiment = result.Experiment,
            seed_case_id = result.SeedCaseId,
            budget_per_trial = result.BudgetPerTrial,
            trials_per_mode = result.TrialsPerMode,
            base_campaign_seed = result.BaseCampaignSeed,
            single_call_maximum_sequence_length = result.SingleCallMaximumSequenceLength,
            stateful_maximum_sequence_length = result.StatefulMaximumSequenceLength,
            provenance,
            trials = result.Trials,
            modes = result.Modes,
            paired_outcomes = result.PairedOutcomes,
            interpretation = "The sequence-length cap is the only experimental variable; no-finding runs are censored at the scheduler limit or execution budget.",
            claims = new
            {
                kernel_crashes = "NOT_CLAIMED",
                real_driver_performance = "NOT_CLAIMED",
                statistical_significance = "NOT_ASSESSED"
            }
        }, cancellationToken).ConfigureAwait(false);

        var raw = new StringBuilder();
        raw.Append("trial,mode,maximum_sequence_length,campaign_seed,budget,executions,found,first_finding_execution,censored,coverage_count,corpus_count,raw_synthetic_failures,exact_signatures\n");
        foreach (var trial in result.Trials)
        {
            raw.Append(BuildRawRow(trial, result.BudgetPerTrial)).Append('\n');
        }

        await WriteBytesAsync(Path.Combine(root, "raw.csv"), Encoding.UTF8.GetBytes(raw.ToString()), cancellationToken).ConfigureAwait(false);
        await WriteBytesAsync(Path.Combine(root, "report", "index.html"), Encoding.UTF8.GetBytes(BuildReport(result)), cancellationToken).ConfigureAwait(false);
        var entries = await EvidenceManifest.CreateAsync(root, cancellationToken).ConfigureAwait(false);
        return new E2ArtifactBuildResult(root, entries.Count);
    }

    public static async Task<E2ArtifactVerificationReport> VerifyAsync(string outputRoot, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        ManifestVerification manifest;
        try
        {
            manifest = await EvidenceManifest.VerifyAsync(outputRoot, cancellationToken).ConfigureAwait(false);
            errors.AddRange(manifest.Errors);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new E2ArtifactVerificationReport(false, 0, [$"Manifest verification failed: {exception.Message}"]);
        }

        var root = Path.GetFullPath(outputRoot);
        var verifiedPaths = manifest.Verified.Select(static entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "summary.json", "raw.csv", "report/index.html" })
        {
            if (!verifiedPaths.Contains(required))
            {
                errors.Add($"Required E2 artifact is missing: {required}");
            }
        }

        try
        {
            using var summary = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json"), cancellationToken).ConfigureAwait(false));
            var summaryRoot = summary.RootElement;
            if (summaryRoot.GetProperty("schema_version").GetInt32() != 1
                || summaryRoot.GetProperty("execution_mode").GetString() != "SIMULATED"
                || summaryRoot.GetProperty("experiment").GetString() != "E2_STATEFUL_VS_SINGLE_CALL_V1")
            {
                errors.Add("summary.json has unsupported E2 metadata.");
            }

            var budget = summaryRoot.GetProperty("budget_per_trial").GetInt32();
            var trialsPerMode = summaryRoot.GetProperty("trials_per_mode").GetInt32();
            var baseSeed = summaryRoot.GetProperty("base_campaign_seed").GetInt64();
            var singleLimit = summaryRoot.GetProperty("single_call_maximum_sequence_length").GetInt32();
            var statefulLimit = summaryRoot.GetProperty("stateful_maximum_sequence_length").GetInt32();
            if (budget < 1 || trialsPerMode < 1 || baseSeed < 0 || singleLimit != 1 || statefulLimit <= singleLimit)
            {
                errors.Add("summary.json contains invalid E2 experiment bounds.");
            }

            var provenance = ExperimentProvenanceBuilder.ParseAndValidate(summaryRoot.GetProperty("provenance"));
            var expectedDigest = ExperimentProvenanceBuilder.E2DefinitionDigest(
                summaryRoot.GetProperty("experiment").GetString() ?? string.Empty,
                summaryRoot.GetProperty("execution_mode").GetString() ?? string.Empty,
                summaryRoot.GetProperty("seed_case_id").GetString() ?? string.Empty,
                budget,
                trialsPerMode,
                baseSeed,
                singleLimit,
                statefulLimit,
                provenance.CaseSchemaVersion);
            if (provenance.ExperimentDefinitionDigest != expectedDigest)
            {
                errors.Add("E2 experiment_definition_digest does not match summary parameters.");
            }

            var trials = summaryRoot.GetProperty("trials").EnumerateArray().ToArray();
            var typedTrials = trials.Select(static trial => trial.Deserialize<E2TrialResult>(ContractJson.Compact)
                ?? throw new InvalidDataException("E2 trial could not be deserialized.")).ToArray();
            if (trials.Length != trialsPerMode * 2)
            {
                errors.Add("E2 trial count does not match the paired design.");
            }

            var seenPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trial in typedTrials)
            {
                var expectedLimit = trial.Mode switch
                {
                    "SINGLE_CALL" => singleLimit,
                    "STATEFUL" => statefulLimit,
                    _ => -1
                };
                if (trial.Trial < 1
                    || trial.Trial > trialsPerMode
                    || !seenPairs.Add($"{trial.Trial}:{trial.Mode}")
                    || trial.MaximumSequenceLength != expectedLimit
                    || trial.CampaignSeed != baseSeed + trial.Trial - 1L
                    || trial.Executions < 1
                    || trial.Executions > budget
                    || trial.Found != trial.FirstFindingExecution.HasValue
                    || trial.Found != (trial.ExactSignatures > 0)
                    || (trial.Found && trial.FirstFindingExecution > trial.Executions))
                {
                    errors.Add($"E2 trial is inconsistent: {trial.Trial}:{trial.Mode}");
                }
            }

            var rawLines = await File.ReadAllLinesAsync(Path.Combine(root, "raw.csv"), cancellationToken).ConfigureAwait(false);
            const string rawHeader = "trial,mode,maximum_sequence_length,campaign_seed,budget,executions,found,first_finding_execution,censored,coverage_count,corpus_count,raw_synthetic_failures,exact_signatures";
            var expectedRows = typedTrials.Select(trial => BuildRawRow(trial, budget)).ToArray();
            if (rawLines.Length != expectedRows.Length + 1
                || rawLines.Length == 0
                || rawLines[0] != rawHeader
                || !rawLines.Skip(1).SequenceEqual(expectedRows, StringComparer.Ordinal))
            {
                errors.Add("E2 raw.csv does not match summary trials.");
            }

            var expectedPaired = E2ExperimentRunner.BuildPairedOutcomes(typedTrials);
            var paired = summaryRoot.GetProperty("paired_outcomes");
            if (paired.GetProperty("both_discovered").GetInt32() != expectedPaired.BothDiscovered
                || paired.GetProperty("stateful_only").GetInt32() != expectedPaired.StatefulOnly
                || paired.GetProperty("single_call_only").GetInt32() != expectedPaired.SingleCallOnly
                || paired.GetProperty("neither_discovered").GetInt32() != expectedPaired.NeitherDiscovered)
            {
                errors.Add("E2 paired outcomes do not match trial records.");
            }

            var claims = summaryRoot.GetProperty("claims");
            if (claims.GetProperty("kernel_crashes").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("real_driver_performance").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("statistical_significance").GetString() != "NOT_ASSESSED")
            {
                errors.Add("summary.json contains unsupported E2 claims.");
            }

            var report = await File.ReadAllTextAsync(Path.Combine(root, "report", "index.html"), cancellationToken).ConfigureAwait(false);
            if (!report.Contains(SimulationBanner, StringComparison.Ordinal))
            {
                errors.Add("E2 report is missing the mandatory simulation banner.");
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or InvalidOperationException
            or KeyNotFoundException
            or DirectoryNotFoundException)
        {
            errors.Add($"Semantic E2 artifact verification failed: {exception.Message}");
        }

        return new E2ArtifactVerificationReport(errors.Count == 0, manifest.Verified.Count, errors);
    }

    private static string BuildRawRow(E2TrialResult trial, int budget) =>
        string.Join(",",
            trial.Trial.ToString(CultureInfo.InvariantCulture),
            trial.Mode,
            trial.MaximumSequenceLength.ToString(CultureInfo.InvariantCulture),
            trial.CampaignSeed.ToString(CultureInfo.InvariantCulture),
            budget.ToString(CultureInfo.InvariantCulture),
            trial.Executions.ToString(CultureInfo.InvariantCulture),
            trial.Found ? "true" : "false",
            trial.FirstFindingExecution?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            trial.Found ? "false" : "true",
            trial.CoverageCount.ToString(CultureInfo.InvariantCulture),
            trial.CorpusCount.ToString(CultureInfo.InvariantCulture),
            trial.RawSyntheticFailures.ToString(CultureInfo.InvariantCulture),
            trial.ExactSignatures.ToString(CultureInfo.InvariantCulture));

    private static string BuildReport(E2ExperimentResult result)
    {
        var rows = string.Join(Environment.NewLine, result.Modes.Select(mode =>
            $"<tr><td>{WebUtility.HtmlEncode(mode.Mode)}</td><td>{mode.MaximumSequenceLength}</td><td>{mode.Discoveries}/{mode.Trials}</td><td>{mode.CensoredTrials}</td><td>{FormatNumber(mode.MedianFirstFindingAmongDiscoveries)} [{FormatNumber(mode.FirstFindingQ1AmongDiscoveries)}, {FormatNumber(mode.FirstFindingQ3AmongDiscoveries)}]</td></tr>"));
        var paired = result.PairedOutcomes;
        return $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>KCrashLab E2 simulated stateful experiment</title><style>
            body{font-family:Segoe UI,system-ui,sans-serif;max-width:980px;margin:40px auto;padding:0 20px;color:#17202a;background:#f4f7f9}.banner{background:#8b1e1e;color:#fff;padding:18px 22px;border-radius:8px;font-weight:750;letter-spacing:.04em}main{background:#fff;margin-top:18px;padding:30px;border-radius:8px;box-shadow:0 8px 28px #10203018}table{width:100%;border-collapse:collapse;margin:18px 0}th,td{text-align:left;border-bottom:1px solid #d8e0e5;padding:12px 8px}.muted{color:#5c6975}</style></head>
            <body><div class="banner">{{SimulationBanner}}</div><main><h1>E2: stateful sequence versus single call</h1>
            <p class="muted">Paired deterministic trials against one synthetic target. The sequence-length cap is the only experimental variable.</p>
            <table><thead><tr><th>Mode</th><th>Maximum operations</th><th>Discoveries</th><th>Censored</th><th>Successful median [Q1, Q3]</th></tr></thead><tbody>{{rows}}</tbody></table>
            <h2>Paired outcomes</h2><table><thead><tr><th>Both</th><th>Stateful only</th><th>Single-call only</th><th>Neither</th></tr></thead><tbody><tr><td>{{paired.BothDiscovered}}</td><td>{{paired.StatefulOnly}}</td><td>{{paired.SingleCallOnly}}</td><td>{{paired.NeitherDiscovered}}</td></tr></tbody></table>
            <p class="muted">The known synthetic signature requires RESET_STATE → SET_MODE(2) → SUBMIT_RECORD(declared length mismatch). Single-call mode cannot express that prerequisite chain. Kernel crashes, real-driver performance, and statistical significance are not claimed.</p>
            </main></body></html>
            """;
    }

    private static string FormatNumber(double? value) => value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a";

    private static JsonSerializerOptions CreateArtifactJson()
    {
        var options = new JsonSerializerOptions(ContractJson.Indented) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
        return options;
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
        await WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, ArtifactJson), cancellationToken).ConfigureAwait(false);

    private static async Task WriteBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Output path has no parent."));
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65_536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
