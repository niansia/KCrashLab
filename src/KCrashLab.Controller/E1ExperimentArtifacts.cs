using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KCrashLab.Contracts;
using KCrashLab.Storage;

namespace KCrashLab.Controller;

public sealed record E1ArtifactBuildResult(string Root, int ManifestEntries);

public sealed record E1ArtifactVerificationReport(bool IsValid, int VerifiedFiles, IReadOnlyList<string> Errors);

public static class E1ExperimentArtifacts
{
    private const string SimulationBanner = "SIMULATED — NOT A REAL KERNEL CRASH BENCHMARK";
    private static readonly IReadOnlySet<string> Strategies = E1ExperimentRunner.Strategies;
    private static readonly JsonSerializerOptions ArtifactJson = CreateArtifactJson();

    public static Task<E1ArtifactBuildResult> BuildAsync(
        string outputRoot,
        E1ExperimentResult result,
        CancellationToken cancellationToken) =>
        BuildAsync(outputRoot, result, ExperimentProvenanceBuilder.UnspecifiedForE1(result), cancellationToken);

    public static async Task<E1ArtifactBuildResult> BuildAsync(
        string outputRoot,
        E1ExperimentResult result,
        ExperimentProvenance provenance,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(result);
        ExperimentProvenanceBuilder.Validate(provenance);
        if (result.SchemaVersion != 2 || result.ExecutionMode != "SIMULATED" || result.Experiment != "E1_POLICY_ABLATION_2X2_V2")
        {
            throw new InvalidDataException("Only simulated E1 2x2 policy-ablation v2 results can be written.");
        }

        var root = Path.GetFullPath(outputRoot);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidDataException("E1 experiment output directory must be empty.");
        }

        Directory.CreateDirectory(root);
        await WriteJsonAsync(Path.Combine(root, "summary.json"), new
        {
            schema_version = result.SchemaVersion,
            execution_mode = result.ExecutionMode,
            experiment = result.Experiment,
            seed_case_id = result.SeedCaseId,
            budget_per_trial = result.BudgetPerTrial,
            trials_per_strategy = result.TrialsPerStrategy,
            base_campaign_seed = result.BaseCampaignSeed,
            provenance,
            trials = result.Trials,
            strategies = result.Strategies,
            factorial_contrasts = result.FactorialContrasts,
            interpretation = "All four arms share one execution engine, operator set, candidate enumeration, and decision-stream construction. Admission and parent-selection policies are crossed 2x2. Discovery medians include successful trials only; censored trials are reported separately.",
            claims = new
            {
                kernel_crashes = "NOT_CLAIMED",
                real_driver_performance = "NOT_CLAIMED",
                statistical_significance = "NOT_ASSESSED"
            }
        }, cancellationToken).ConfigureAwait(false);

        var raw = new StringBuilder();
        raw.Append("trial,strategy,campaign_seed,budget,executions,found,first_finding_execution,censored,coverage_count,corpus_count,raw_synthetic_failures,exact_signatures\n");
        foreach (var trial in result.Trials)
        {
            raw.Append(trial.Trial.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(trial.Strategy).Append(',')
                .Append(trial.CampaignSeed.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.BudgetPerTrial.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(trial.Executions.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(trial.Found ? "true" : "false").Append(',')
                .Append(trial.FirstFindingExecution?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(trial.Found ? "false" : "true").Append(',')
                .Append(trial.CoverageCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(trial.CorpusCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(trial.RawSyntheticFailures.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(trial.ExactSignatures.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        await WriteBytesAsync(Path.Combine(root, "raw.csv"), ArtifactText.Encode(raw.ToString()), cancellationToken).ConfigureAwait(false);
        var survival = new StringBuilder();
        survival.Append("strategy,execution,at_risk,discoveries,censored,survival_probability,cumulative_discovery_probability\n");
        foreach (var point in result.SurvivalCurve)
        {
            survival.Append(BuildSurvivalRow(point)).Append('\n');
        }

        await WriteBytesAsync(Path.Combine(root, "survival.csv"), ArtifactText.Encode(survival.ToString()), cancellationToken).ConfigureAwait(false);
        await WriteBytesAsync(Path.Combine(root, "report", "index.html"), ArtifactText.Encode(BuildReport(result)), cancellationToken).ConfigureAwait(false);
        var entries = await EvidenceManifest.CreateAsync(root, cancellationToken).ConfigureAwait(false);
        return new E1ArtifactBuildResult(root, entries.Count);
    }

    public static async Task<E1ArtifactVerificationReport> VerifyAsync(
        string outputRoot,
        CancellationToken cancellationToken)
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
            return new E1ArtifactVerificationReport(false, 0, [$"Manifest verification failed: {exception.Message}"]);
        }

        var root = Path.GetFullPath(outputRoot);
        var verifiedPaths = manifest.Verified.Select(static entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "summary.json", "raw.csv", "survival.csv", "report/index.html" })
        {
            if (!verifiedPaths.Contains(required))
            {
                errors.Add($"Required E1 artifact is missing: {required}");
            }
        }

        try
        {
            using var summary = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json"), cancellationToken).ConfigureAwait(false));
            var summaryRoot = summary.RootElement;
            if (summaryRoot.GetProperty("schema_version").GetInt32() != 2
                || summaryRoot.GetProperty("execution_mode").GetString() != "SIMULATED"
                || summaryRoot.GetProperty("experiment").GetString() != "E1_POLICY_ABLATION_2X2_V2")
            {
                errors.Add("summary.json has unsupported E1 metadata.");
            }

            var budget = summaryRoot.GetProperty("budget_per_trial").GetInt32();
            var trialsPerStrategy = summaryRoot.GetProperty("trials_per_strategy").GetInt32();
            var baseCampaignSeed = summaryRoot.GetProperty("base_campaign_seed").GetInt64();
            if (budget < 1 || trialsPerStrategy < 1 || baseCampaignSeed < 0)
            {
                errors.Add("summary.json contains invalid experiment bounds.");
            }

            var provenance = ExperimentProvenanceBuilder.ParseAndValidate(summaryRoot.GetProperty("provenance"));
            var expectedDefinitionDigest = ExperimentProvenanceBuilder.E1DefinitionDigest(
                summaryRoot.GetProperty("experiment").GetString() ?? string.Empty,
                summaryRoot.GetProperty("execution_mode").GetString() ?? string.Empty,
                summaryRoot.GetProperty("seed_case_id").GetString() ?? string.Empty,
                budget,
                trialsPerStrategy,
                baseCampaignSeed,
                provenance.CaseSchemaVersion);
            if (provenance.ExperimentDefinitionDigest != expectedDefinitionDigest)
            {
                errors.Add("E1 experiment_definition_digest does not match summary parameters.");
            }

            var seenPairs = new HashSet<string>(StringComparer.Ordinal);
            var trials = summaryRoot.GetProperty("trials").EnumerateArray().ToArray();
            if (trials.Length != trialsPerStrategy * Strategies.Count)
            {
                errors.Add("Trial count does not match the paired 2x2 factorial design.");
            }

            foreach (var trial in trials)
            {
                var trialNumber = trial.GetProperty("trial").GetInt32();
                var strategy = trial.GetProperty("strategy").GetString() ?? throw new InvalidDataException("Trial strategy is null.");
                var found = trial.GetProperty("found").GetBoolean();
                var first = trial.GetProperty("first_finding_execution");
                var executions = trial.GetProperty("executions").GetInt32();
                var exactSignatures = trial.GetProperty("exact_signatures").GetInt32();
                if (trialNumber < 1 || trialNumber > trialsPerStrategy || !Strategies.Contains(strategy))
                {
                    errors.Add("Trial has an invalid number or strategy.");
                }

                if (!seenPairs.Add($"{trialNumber}:{strategy}"))
                {
                    errors.Add($"Duplicate trial/strategy pair: {trialNumber}:{strategy}");
                }

                if (trial.GetProperty("campaign_seed").GetInt64() != baseCampaignSeed + trialNumber - 1L
                    || executions < 1
                    || executions > budget)
                {
                    errors.Add($"Trial bounds or paired seed are invalid: {trialNumber}:{strategy}");
                }

                if (found != (first.ValueKind == JsonValueKind.Number)
                    || found != (exactSignatures > 0)
                    || (found && (first.GetInt32() < 1 || first.GetInt32() > executions)))
                {
                    errors.Add($"Trial finding outcome is inconsistent: {trialNumber}:{strategy}");
                }
            }

            foreach (var trialNumber in Enumerable.Range(1, trialsPerStrategy))
            {
                foreach (var strategy in Strategies)
                {
                    if (!seenPairs.Contains($"{trialNumber}:{strategy}"))
                    {
                        errors.Add($"Missing trial/strategy pair: {trialNumber}:{strategy}");
                    }
                }
            }

            var rawLines = await File.ReadAllLinesAsync(Path.Combine(root, "raw.csv"), cancellationToken).ConfigureAwait(false);
            const string rawHeader = "trial,strategy,campaign_seed,budget,executions,found,first_finding_execution,censored,coverage_count,corpus_count,raw_synthetic_failures,exact_signatures";
            if (rawLines.Length != trials.Length + 1 || rawLines.Length == 0 || rawLines[0] != rawHeader)
            {
                errors.Add("raw.csv shape does not match summary trials.");
            }
            else
            {
                var expectedRows = trials.Select(trial => BuildRawRow(trial, budget)).ToArray();
                if (!rawLines.Skip(1).SequenceEqual(expectedRows, StringComparer.Ordinal))
                {
                    errors.Add("raw.csv rows do not match summary trial records.");
                }
            }

            var strategySummaries = summaryRoot.GetProperty("strategies").EnumerateArray().ToArray();
            if (strategySummaries.Length != Strategies.Count
                || !strategySummaries.Select(static item => item.GetProperty("strategy").GetString()).ToHashSet(StringComparer.Ordinal).SetEquals(Strategies))
            {
                errors.Add("Strategy summaries do not cover all four E1 policy combinations.");
            }

            foreach (var strategySummary in strategySummaries)
            {
                var strategy = strategySummary.GetProperty("strategy").GetString()
                    ?? throw new InvalidDataException("Summary strategy is null.");
                var matchingTrials = trials.Where(trial => trial.GetProperty("strategy").GetString() == strategy).ToArray();
                if (matchingTrials.Length == 0)
                {
                    errors.Add($"Strategy summary has no matching trials: {strategy}");
                    continue;
                }

                var firstFindings = matchingTrials
                    .Where(static trial => trial.GetProperty("found").GetBoolean())
                    .Select(static trial => trial.GetProperty("first_finding_execution").GetInt32())
                    .Order()
                    .ToArray();
                if (strategySummary.GetProperty("trials").GetInt32() != matchingTrials.Length
                    || strategySummary.GetProperty("discoveries").GetInt32() != firstFindings.Length
                    || strategySummary.GetProperty("censored_trials").GetInt32() != matchingTrials.Length - firstFindings.Length
                    || !NearlyEqual(strategySummary.GetProperty("discovery_rate").GetDouble(), (double)firstFindings.Length / matchingTrials.Length)
                    || !NullableEqual(strategySummary.GetProperty("median_first_finding_among_discoveries"), Quantile(firstFindings, 0.5))
                    || !NullableEqual(strategySummary.GetProperty("first_finding_q1_among_discoveries"), Quantile(firstFindings, 0.25))
                    || !NullableEqual(strategySummary.GetProperty("first_finding_q3_among_discoveries"), Quantile(firstFindings, 0.75)))
                {
                    errors.Add($"Strategy summary does not match raw trials: {strategy}");
                }
            }

            var typedTrials = trials
                .Select(static trial => trial.Deserialize<E1TrialResult>(ContractJson.Compact)
                    ?? throw new InvalidDataException("Trial could not be deserialized."))
                .ToArray();
            var expectedContrasts = E1ExperimentRunner.BuildFactorialContrasts(typedTrials);
            var actualContrasts = summaryRoot.GetProperty("factorial_contrasts")
                .EnumerateArray()
                .Select(static item => item.Deserialize<E1FactorialContrast>(ContractJson.Compact)
                    ?? throw new InvalidDataException("Factorial contrast could not be deserialized."))
                .ToArray();
            if (!actualContrasts.SequenceEqual(expectedContrasts))
            {
                errors.Add("Factorial contrasts do not match paired trial records.");
            }

            var expectedSurvival = E1ExperimentRunner.BuildSurvivalCurve(typedTrials, budget)
                .Select(BuildSurvivalRow)
                .ToArray();
            var survivalLines = await File.ReadAllLinesAsync(Path.Combine(root, "survival.csv"), cancellationToken).ConfigureAwait(false);
            const string survivalHeader = "strategy,execution,at_risk,discoveries,censored,survival_probability,cumulative_discovery_probability";
            if (survivalLines.Length != expectedSurvival.Length + 1
                || survivalLines.Length == 0
                || survivalLines[0] != survivalHeader
                || !survivalLines.Skip(1).SequenceEqual(expectedSurvival, StringComparer.Ordinal))
            {
                errors.Add("survival.csv does not match the censoring-aware analysis of raw trials.");
            }

            var claims = summaryRoot.GetProperty("claims");
            if (claims.GetProperty("kernel_crashes").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("real_driver_performance").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("statistical_significance").GetString() != "NOT_ASSESSED")
            {
                errors.Add("summary.json contains unsupported experiment claims.");
            }

            var report = await File.ReadAllTextAsync(Path.Combine(root, "report", "index.html"), cancellationToken).ConfigureAwait(false);
            if (!report.Contains(SimulationBanner, StringComparison.Ordinal))
            {
                errors.Add("E1 report is missing the mandatory simulation banner.");
            }

            if (Directory.EnumerateFiles(root, "*.dmp", SearchOption.AllDirectories).Any())
            {
                errors.Add("A simulated E1 experiment must not contain a memory dump.");
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
            errors.Add($"Semantic E1 artifact verification failed: {exception.Message}");
        }

        return new E1ArtifactVerificationReport(errors.Count == 0, manifest.Verified.Count, errors);
    }

    private static string BuildReport(E1ExperimentResult result)
    {
        var rows = string.Join(
            "\n",
            result.Strategies.Select(summary => $"<tr><td><code>{WebUtility.HtmlEncode(summary.Strategy)}</code></td><td>{summary.Discoveries}/{summary.Trials}</td><td>{summary.CensoredTrials}</td><td>{FormatNumber(summary.MedianFirstFindingAmongDiscoveries)} [{FormatNumber(summary.FirstFindingQ1AmongDiscoveries)}, {FormatNumber(summary.FirstFindingQ3AmongDiscoveries)}]</td></tr>"));
        var contrasts = string.Join(
            "\n",
            result.FactorialContrasts.Select(contrast => $"<tr><td><code>{WebUtility.HtmlEncode(contrast.Contrast)}</code></td><td><code>{WebUtility.HtmlEncode(contrast.LeftStrategy)}</code></td><td><code>{WebUtility.HtmlEncode(contrast.RightStrategy)}</code></td><td>{contrast.BothDiscovered}</td><td>{contrast.LeftOnly}</td><td>{contrast.RightOnly}</td><td>{contrast.NeitherDiscovered}</td></tr>"));
        return FormattableString.Invariant($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>KCrashLab E1 simulated 2x2 policy ablation</title>
              <style>
                body{font-family:Segoe UI,system-ui,sans-serif;max-width:980px;margin:40px auto;padding:0 20px;color:#17202a;background:#f4f7f9}
                .banner{background:#8b1e1e;color:#fff;padding:18px 22px;border-radius:8px;font-weight:750;letter-spacing:.04em}
                main{background:#fff;margin-top:18px;padding:30px;border-radius:8px;box-shadow:0 8px 28px #10203018}
                table{width:100%;border-collapse:collapse;margin:18px 0}th,td{text-align:left;border-bottom:1px solid #d8e0e5;padding:12px 8px}.muted{color:#5c6975}code{overflow-wrap:anywhere}.chart{width:100%;height:auto;background:#fbfcfd;border:1px solid #d8e0e5;border-radius:6px}
              </style>
            </head>
            <body>
              <div class="banner">{{SimulationBanner}}</div>
              <main>
                <h1>E1: 2×2 policy ablation</h1>
                <p class="muted">Paired deterministic trials against one synthetic state-machine target. Corpus admission (keep-all/novelty-only) is crossed with parent selection (uniform/energy). Operator and candidate selection remain uniform in every arm. This report makes no claim about real drivers or statistical significance.</p>
                <p>Budget: <strong>{{result.BudgetPerTrial}}</strong> executions per trial; paired trials: <strong>{{result.TrialsPerStrategy}}</strong>; base seed: <code>{{result.BaseCampaignSeed}}</code>.</p>
                <table><thead><tr><th>Strategy</th><th>Discoveries</th><th>Censored</th><th>Median [Q1, Q3] first finding*</th></tr></thead><tbody>{{rows}}</tbody></table>
                <p class="muted">* Quantiles use linear interpolation and include successful trials only. No-finding trials are right-censored at the fixed budget and listed separately.</p>
                <h2>Censoring-aware discovery curve</h2>
                {{BuildSurvivalSvg(result)}}
                <p class="muted">The step curve is 1 − Kaplan–Meier survival. Censored trials remain in the risk set through execution {{result.BudgetPerTrial}}.</p>
                <h2>Planned paired contrasts</h2>
                <table><thead><tr><th>Contrast</th><th>Left</th><th>Right</th><th>Both</th><th>Left only</th><th>Right only</th><th>Neither</th></tr></thead><tbody>{{contrasts}}</tbody></table>
                <p class="muted">These counts are descriptive. Statistical significance and external validity are not assessed.</p>
              </main>
            </body>
            </html>
            """);
    }

    private static string FormatNumber(double? value) =>
        value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a";

    private static bool NullableEqual(JsonElement element, double? expected) =>
        expected.HasValue
            ? element.ValueKind == JsonValueKind.Number && NearlyEqual(element.GetDouble(), expected.Value)
            : element.ValueKind == JsonValueKind.Null;

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.000000001;

    private static double? Quantile(int[] values, double probability)
    {
        if (values.Length == 0)
        {
            return null;
        }

        var position = (values.Length - 1) * probability;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? values[lower]
            : values[lower] + ((values[upper] - values[lower]) * (position - lower));
    }

    private static string BuildSurvivalRow(E1SurvivalPoint point) =>
        string.Join(",",
            point.Strategy,
            point.Execution.ToString(CultureInfo.InvariantCulture),
            point.AtRisk.ToString(CultureInfo.InvariantCulture),
            point.Discoveries.ToString(CultureInfo.InvariantCulture),
            point.Censored.ToString(CultureInfo.InvariantCulture),
            point.SurvivalProbability.ToString("R", CultureInfo.InvariantCulture),
            point.CumulativeDiscoveryProbability.ToString("R", CultureInfo.InvariantCulture));

    private static string BuildSurvivalSvg(E1ExperimentResult result)
    {
        const double left = 64;
        const double right = 864;
        const double top = 30;
        const double bottom = 330;
        var colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [E1ExperimentRunner.KeepAllUniform] = "#c25b30",
            [E1ExperimentRunner.KeepAllEnergyRanked] = "#8c4f9e",
            [E1ExperimentRunner.NoveltyUniform] = "#2878b5",
            [E1ExperimentRunner.NoveltyEnergyRanked] = "#116466"
        };
        var paths = new StringBuilder();
        foreach (var group in result.SurvivalCurve.GroupBy(static point => point.Strategy, StringComparer.Ordinal))
        {
            var points = group.OrderBy(static point => point.Execution).ToArray();
            var path = new StringBuilder();
            var currentY = bottom;
            path.Append("M ").Append(FormatCoordinate(left)).Append(' ').Append(FormatCoordinate(currentY));
            foreach (var point in points.Skip(1))
            {
                var x = left + ((right - left) * point.Execution / result.BudgetPerTrial);
                var y = bottom - ((bottom - top) * point.CumulativeDiscoveryProbability);
                path.Append(" H ").Append(FormatCoordinate(x)).Append(" V ").Append(FormatCoordinate(y));
                currentY = y;
            }

            path.Append(" H ").Append(FormatCoordinate(right));
            paths.Append("<path d=\"").Append(path).Append("\" fill=\"none\" stroke=\"")
                .Append(colors[group.Key]).Append("\" stroke-width=\"4\"/>");
        }

        return $$"""
            <svg class="chart" viewBox="0 0 920 390" role="img" aria-label="Cumulative discovery probability by execution">
              <line x1="64" y1="30" x2="64" y2="330" stroke="#667784"/><line x1="64" y1="330" x2="864" y2="330" stroke="#667784"/>
              <line x1="64" y1="180" x2="864" y2="180" stroke="#d8e0e5" stroke-dasharray="5 5"/>
              <text x="38" y="334" font-size="13">0</text><text x="28" y="184" font-size="13">0.5</text><text x="28" y="35" font-size="13">1.0</text>
              <text x="60" y="356" font-size="13">0</text><text x="830" y="356" font-size="13">{{result.BudgetPerTrial}}</text>
              {{paths}}
              <line x1="70" y1="374" x2="100" y2="374" stroke="#c25b30" stroke-width="4"/><text x="106" y="379" font-size="12">keep/uniform</text>
              <line x1="260" y1="374" x2="290" y2="374" stroke="#8c4f9e" stroke-width="4"/><text x="296" y="379" font-size="12">keep/energy</text>
              <line x1="445" y1="374" x2="475" y2="374" stroke="#2878b5" stroke-width="4"/><text x="481" y="379" font-size="12">novelty/uniform</text>
              <line x1="650" y1="374" x2="680" y2="374" stroke="#116466" stroke-width="4"/><text x="686" y="379" font-size="12">novelty/energy</text>
            </svg>
            """;
    }

    private static string FormatCoordinate(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string BuildRawRow(JsonElement trial, int budget)
    {
        var found = trial.GetProperty("found").GetBoolean();
        var first = trial.GetProperty("first_finding_execution");
        return string.Join(",",
            trial.GetProperty("trial").GetInt32().ToString(CultureInfo.InvariantCulture),
            trial.GetProperty("strategy").GetString(),
            trial.GetProperty("campaign_seed").GetInt64().ToString(CultureInfo.InvariantCulture),
            budget.ToString(CultureInfo.InvariantCulture),
            trial.GetProperty("executions").GetInt32().ToString(CultureInfo.InvariantCulture),
            found ? "true" : "false",
            first.ValueKind == JsonValueKind.Number ? first.GetInt32().ToString(CultureInfo.InvariantCulture) : string.Empty,
            found ? "false" : "true",
            trial.GetProperty("coverage_count").GetInt32().ToString(CultureInfo.InvariantCulture),
            trial.GetProperty("corpus_count").GetInt32().ToString(CultureInfo.InvariantCulture),
            trial.GetProperty("raw_synthetic_failures").GetInt32().ToString(CultureInfo.InvariantCulture),
            trial.GetProperty("exact_signatures").GetInt32().ToString(CultureInfo.InvariantCulture));
    }

    private static async Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
        await WriteBytesAsync(path, ArtifactText.SerializeJson(value, ArtifactJson), cancellationToken).ConfigureAwait(false);

    private static JsonSerializerOptions CreateArtifactJson()
    {
        var options = new JsonSerializerOptions(ContractJson.Indented)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        return options;
    }

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
