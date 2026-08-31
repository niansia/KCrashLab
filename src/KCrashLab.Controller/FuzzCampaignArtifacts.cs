using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Domain;
using KCrashLab.Storage;

namespace KCrashLab.Controller;

public sealed record FuzzArtifactBuildResult(
    string Root,
    int ManifestEntries,
    int CorpusCases,
    int ExactFindings);

public sealed record FuzzArtifactVerificationReport(
    bool IsValid,
    int VerifiedFiles,
    IReadOnlyList<string> Errors);

public static class FuzzCampaignArtifacts
{
    private const string SimulationBanner = "SIMULATED — NOT A REAL KERNEL CRASH";

    public static Task<FuzzArtifactBuildResult> BuildAsync(
        string outputRoot,
        FuzzCampaignResult result,
        CancellationToken cancellationToken) =>
        BuildAsync(outputRoot, result, ExperimentProvenanceBuilder.UnspecifiedForFuzz(result), cancellationToken);

    public static async Task<FuzzArtifactBuildResult> BuildAsync(
        string outputRoot,
        FuzzCampaignResult result,
        ExperimentProvenance provenance,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(result);
        ExperimentProvenanceBuilder.Validate(provenance);
        if (result.ExecutionMode != "SIMULATED" || result.SchemaVersion != 1)
        {
            throw new InvalidDataException("Only simulated fuzz campaign v1 results can be written.");
        }

        var root = Path.GetFullPath(outputRoot);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidDataException("Fuzz campaign output directory must be empty.");
        }

        Directory.CreateDirectory(root);
        await WriteJsonAsync(Path.Combine(root, "summary.json"), new
        {
            schema_version = 1,
            execution_mode = "SIMULATED",
            strategy = result.Strategy,
            campaign_seed = result.CampaignSeed,
            budget = result.Budget,
            executions = result.Executions,
            termination_reason = result.TerminationReason,
            scheduling_policy = result.SchedulingPolicy,
            scheduling_iterations = result.SchedulingIterations,
            scheduling_limit = result.SchedulingLimit,
            duplicate_candidate_skips = result.DuplicateCandidateSkips,
            empty_candidate_polls = result.EmptyCandidatePolls,
            candidate_enumeration = result.CandidateEnumeration,
            max_candidates = result.MaximumCandidatesPerOperator,
            seed_case_id = result.SeedCaseId,
            corpus_count = result.Corpus.Count,
            coverage_count = result.GlobalCoverage.Count,
            raw_synthetic_failures = result.Findings.Sum(static item => item.Occurrences),
            exact_signatures = result.Findings.Count,
            provenance,
            findings = result.Findings.Select(static finding => new
            {
                signature = finding.Signature,
                first_case_id = finding.FirstCaseId,
                first_execution = finding.FirstExecution,
                occurrences = finding.Occurrences
            }).ToArray(),
            claims = new
            {
                kernel_crashes = "NOT_CLAIMED",
                root_causes = "NOT_CLAIMED",
                exploitability = "NOT_ASSESSED"
            }
        }, cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(Path.Combine(root, "coverage.json"), new
        {
            schema_version = 1,
            execution_mode = "SIMULATED",
            elements = result.GlobalCoverage
        }, cancellationToken).ConfigureAwait(false);

        var metrics = new StringBuilder();
        metrics.Append("execution,case_id,parent_case_id,operator_id,novel_coverage,added_to_corpus,result_class,signature\n");
        foreach (var execution in result.ExecutionLog)
        {
            metrics.Append(execution.Execution.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(execution.CaseId)).Append(',')
                .Append(Csv(execution.ParentCaseId)).Append(',')
                .Append(Csv(execution.OperatorId)).Append(',')
                .Append(execution.NovelCoverage.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(execution.AddedToCorpus ? "true" : "false").Append(',')
                .Append(Csv(execution.ResultClass)).Append(',')
                .Append(Csv(execution.Signature)).Append('\n');
        }

        await WriteBytesAsync(Path.Combine(root, "metrics.csv"), ArtifactText.Encode(metrics.ToString()), cancellationToken).ConfigureAwait(false);

        foreach (var corpusEntry in result.Corpus)
        {
            await WriteBytesAsync(
                Path.Combine(root, "corpus", corpusEntry.TestCase.CaseId + ".case.json"),
                corpusEntry.TestCase.CanonicalUtf8,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var finding in result.Findings)
        {
            var findingRoot = Path.Combine(root, "findings", finding.Signature);
            await WriteBytesAsync(
                Path.Combine(findingRoot, "trigger.case.json"),
                finding.Representative.CanonicalUtf8,
                cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(Path.Combine(findingRoot, "finding.json"), new
            {
                schema_version = 1,
                execution_mode = "SIMULATED",
                signature_version = 1,
                signature = finding.Signature,
                first_case_id = finding.FirstCaseId,
                first_execution = finding.FirstExecution,
                occurrences = finding.Occurrences,
                classification = "SYNTHETIC_DISCOVERY",
                kernel_crash = "NOT_CLAIMED",
                root_cause = "NOT_CLAIMED",
                exploitability = "NOT_ASSESSED"
            }, cancellationToken).ConfigureAwait(false);
        }

        await WriteBytesAsync(
            Path.Combine(root, "report", "index.html"),
            ArtifactText.Encode(BuildReport(result)),
            cancellationToken).ConfigureAwait(false);
        var entries = await EvidenceManifest.CreateAsync(root, cancellationToken).ConfigureAwait(false);
        return new FuzzArtifactBuildResult(root, entries.Count, result.Corpus.Count, result.Findings.Count);
    }

    public static async Task<FuzzArtifactVerificationReport> VerifyAsync(
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
            return new FuzzArtifactVerificationReport(false, 0, [$"Manifest verification failed: {exception.Message}"]);
        }

        var root = Path.GetFullPath(outputRoot);
        var verifiedPaths = manifest.Verified.Select(static entry => entry.RelativePath).ToHashSet(StringComparer.Ordinal);
        foreach (var required in new[] { "summary.json", "coverage.json", "metrics.csv", "report/index.html" })
        {
            if (!verifiedPaths.Contains(required))
            {
                errors.Add($"Required fuzz campaign file is missing: {required}");
            }
        }

        try
        {
            using var summary = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json"), cancellationToken).ConfigureAwait(false));
            var summaryRoot = summary.RootElement;
            if (summaryRoot.GetProperty("schema_version").GetInt32() != 1)
            {
                errors.Add("summary.json has an unsupported schema version.");
            }

            if (summaryRoot.GetProperty("execution_mode").GetString() != "SIMULATED")
            {
                errors.Add("summary.json does not declare SIMULATED execution mode.");
            }

            if (summaryRoot.GetProperty("strategy").GetString() is not ("KEEP_ALL_UNIFORM_V2" or "KEEP_ALL_ENERGY_RANKED_V2" or "NOVELTY_ONLY_UNIFORM_V2" or "NOVELTY_ONLY_ENERGY_RANKED_V2"))
            {
                errors.Add("summary.json has an unsupported fuzzing strategy.");
            }

            var budget = summaryRoot.GetProperty("budget").GetInt32();
            var executions = summaryRoot.GetProperty("executions").GetInt32();
            if (budget < 1 || executions < 1 || executions > budget)
            {
                errors.Add("summary.json contains invalid budget or execution counts.");
            }
            var terminationReason = summaryRoot.GetProperty("termination_reason").GetString();
            var schedulingPolicy = summaryRoot.GetProperty("scheduling_policy").GetString();
            var schedulingIterations = summaryRoot.GetProperty("scheduling_iterations").GetInt32();
            var schedulingLimit = summaryRoot.GetProperty("scheduling_limit").GetInt32();
            var expectedSchedulingLimit = FuzzSchedulingPolicy.ComputeIterationLimit(
                budget,
                DefaultMutationOperators.Create().Count);
            if (terminationReason is not ("BUDGET_REACHED" or "SCHEDULER_ITERATION_LIMIT_REACHED")
                || schedulingPolicy != FuzzSchedulingPolicy.AlgorithmId
                || schedulingIterations < 0 || schedulingIterations > schedulingLimit
                || (terminationReason == "BUDGET_REACHED") != (executions == budget)
                || (terminationReason == "SCHEDULER_ITERATION_LIMIT_REACHED" && schedulingIterations != schedulingLimit)
                || schedulingLimit != expectedSchedulingLimit
                || summaryRoot.GetProperty("candidate_enumeration").GetString() != MutationCandidateSampling.AlgorithmId
                || summaryRoot.GetProperty("max_candidates").GetInt32() != MutationCandidateSampling.DefaultMaximumCandidatesPerOperator
                || summaryRoot.GetProperty("duplicate_candidate_skips").GetInt32() < 0
                || summaryRoot.GetProperty("empty_candidate_polls").GetInt32() < 0)
            {
                errors.Add("summary.json contains invalid scheduler termination telemetry.");
            }

            if (summaryRoot.GetProperty("campaign_seed").GetInt64() < 0)
            {
                errors.Add("summary.json contains a negative campaign seed.");
            }

            var provenance = ExperimentProvenanceBuilder.ParseAndValidate(summaryRoot.GetProperty("provenance"));
            var expectedDefinitionDigest = ExperimentProvenanceBuilder.FuzzDefinitionDigest(
                summaryRoot.GetProperty("execution_mode").GetString() ?? string.Empty,
                summaryRoot.GetProperty("strategy").GetString() ?? string.Empty,
                summaryRoot.GetProperty("campaign_seed").GetInt64(),
                budget,
                summaryRoot.GetProperty("seed_case_id").GetString() ?? string.Empty,
                provenance.CaseSchemaVersion);
            if (provenance.ExperimentDefinitionDigest != expectedDefinitionDigest)
            {
                errors.Add("Fuzz experiment_definition_digest does not match summary parameters.");
            }

            var metricsLines = await File.ReadAllLinesAsync(Path.Combine(root, "metrics.csv"), cancellationToken).ConfigureAwait(false);
            if (metricsLines.Length != executions + 1)
            {
                errors.Add("metrics.csv row count does not match summary executions.");
            }

            const string expectedMetricsHeader = "execution,case_id,parent_case_id,operator_id,novel_coverage,added_to_corpus,result_class,signature";
            if (metricsLines.Length == 0 || metricsLines[0] != expectedMetricsHeader)
            {
                errors.Add("metrics.csv has an unexpected header.");
            }

            using var coverage = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "coverage.json"), cancellationToken).ConfigureAwait(false));
            var coverageRoot = coverage.RootElement;
            if (coverageRoot.GetProperty("schema_version").GetInt32() != 1
                || coverageRoot.GetProperty("execution_mode").GetString() != "SIMULATED")
            {
                errors.Add("coverage.json has unsupported metadata.");
            }

            var coverageElements = coverageRoot.GetProperty("elements")
                .EnumerateArray()
                .Select(static element => element.GetString() ?? throw new InvalidDataException("Coverage element is null."))
                .ToArray();
            if (coverageElements.Length != summaryRoot.GetProperty("coverage_count").GetInt32()
                || coverageElements.Distinct(StringComparer.Ordinal).Count() != coverageElements.Length
                || !coverageElements.SequenceEqual(coverageElements.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                errors.Add("coverage.json is not a sorted unique set matching summary coverage_count.");
            }

            var corpusFiles = Directory.EnumerateFiles(Path.Combine(root, "corpus"), "*.case.json", SearchOption.TopDirectoryOnly).ToArray();
            var corpusIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in corpusFiles)
            {
                var testCase = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
                var fileName = Path.GetFileName(path);
                if (!string.Equals(fileName, testCase.CaseId + ".case.json", StringComparison.Ordinal))
                {
                    errors.Add($"Corpus filename does not match semantic case ID: {fileName}");
                }

                corpusIds.Add(testCase.CaseId);
            }

            if (corpusIds.Count != summaryRoot.GetProperty("corpus_count").GetInt32())
            {
                errors.Add("Corpus file count does not match summary corpus_count.");
            }

            foreach (var path in corpusFiles)
            {
                var testCase = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
                if (testCase.Value.ParentCaseId is { } parent && !corpusIds.Contains(parent))
                {
                    errors.Add($"Corpus lineage parent is absent: {parent}");
                }
            }

            var findingElements = summaryRoot.GetProperty("findings").EnumerateArray().ToArray();
            if (findingElements.Length != summaryRoot.GetProperty("exact_signatures").GetInt32()
                || findingElements.Sum(static finding => finding.GetProperty("occurrences").GetInt32())
                    != summaryRoot.GetProperty("raw_synthetic_failures").GetInt32())
            {
                errors.Add("Finding counts do not match summary totals.");
            }

            var summarySignatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var findingElement in findingElements)
            {
                var signature = findingElement.GetProperty("signature").GetString()
                    ?? throw new InvalidDataException("Finding signature is null.");
                if (!summarySignatures.Add(signature))
                {
                    errors.Add($"Duplicate finding signature: {signature}");
                }

                var triggerPath = Path.Combine(root, "findings", signature, "trigger.case.json");
                var findingPath = Path.Combine(root, "findings", signature, "finding.json");
                if (!File.Exists(triggerPath) || !File.Exists(findingPath))
                {
                    errors.Add($"Finding artifact set is incomplete: {signature}");
                    continue;
                }

                var trigger = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(triggerPath, cancellationToken).ConfigureAwait(false));
                if (trigger.CaseId != findingElement.GetProperty("first_case_id").GetString())
                {
                    errors.Add($"Finding trigger case ID mismatch: {signature}");
                }

                using var finding = JsonDocument.Parse(await File.ReadAllBytesAsync(findingPath, cancellationToken).ConfigureAwait(false));
                var findingRoot = finding.RootElement;
                if (findingRoot.GetProperty("schema_version").GetInt32() != 1
                    || findingRoot.GetProperty("signature_version").GetInt32() != 1
                    || findingRoot.GetProperty("execution_mode").GetString() != "SIMULATED"
                    || findingRoot.GetProperty("signature").GetString() != signature
                    || findingRoot.GetProperty("first_case_id").GetString() != trigger.CaseId
                    || findingRoot.GetProperty("kernel_crash").GetString() != "NOT_CLAIMED"
                    || findingRoot.GetProperty("root_cause").GetString() != "NOT_CLAIMED"
                    || findingRoot.GetProperty("exploitability").GetString() != "NOT_ASSESSED")
                {
                    errors.Add($"Finding metadata is inconsistent: {signature}");
                }
            }

            var findingRootPath = Path.Combine(root, "findings");
            var artifactSignatures = Directory.Exists(findingRootPath)
                ? Directory.EnumerateDirectories(findingRootPath)
                    .Select(static path => new DirectoryInfo(path).Name)
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (!artifactSignatures.SetEquals(summarySignatures))
            {
                errors.Add("Finding directories do not match summary signatures.");
            }

            var claims = summaryRoot.GetProperty("claims");
            if (claims.GetProperty("kernel_crashes").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("root_causes").GetString() != "NOT_CLAIMED"
                || claims.GetProperty("exploitability").GetString() != "NOT_ASSESSED")
            {
                errors.Add("summary.json contains unsupported claims.");
            }

            var report = await File.ReadAllTextAsync(Path.Combine(root, "report", "index.html"), cancellationToken).ConfigureAwait(false);
            if (!report.Contains(SimulationBanner, StringComparison.Ordinal))
            {
                errors.Add("Fuzz report is missing the mandatory simulation banner.");
            }

            if (Directory.EnumerateFiles(root, "*.dmp", SearchOption.AllDirectories).Any())
            {
                errors.Add("A simulated fuzz campaign must not contain a memory dump.");
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
            errors.Add($"Semantic fuzz artifact verification failed: {exception.Message}");
        }

        return new FuzzArtifactVerificationReport(errors.Count == 0, manifest.Verified.Count, errors);
    }

    private static string BuildReport(FuzzCampaignResult result)
    {
        var firstFinding = result.Findings.Count == 0 ? null : result.Findings[0];
        var firstFindingText = firstFinding is null
            ? "No synthetic finding in budget"
            : $"Execution {firstFinding.FirstExecution}: {WebUtility.HtmlEncode(firstFinding.Signature)}";
        return FormattableString.Invariant($$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>KCrashLab simulated fuzz campaign</title>
              <style>
                body{font-family:Segoe UI,system-ui,sans-serif;max-width:980px;margin:40px auto;padding:0 20px;color:#17202a;background:#f4f7f9}
                .banner{background:#8b1e1e;color:#fff;padding:18px 22px;border-radius:8px;font-weight:750;letter-spacing:.04em}
                main{background:#fff;margin-top:18px;padding:30px;border-radius:8px;box-shadow:0 8px 28px #10203018}
                .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px}.metric{background:#eef3f6;padding:16px;border-radius:6px}
                .metric strong{display:block;font-size:1.8rem}.muted{color:#5c6975}code{overflow-wrap:anywhere}
              </style>
            </head>
            <body>
              <div class="banner">{{SimulationBanner}}</div>
              <main>
                <h1>Novelty-guided fuzz campaign</h1>
                <p class="muted">Deterministic synthetic target research output. Kernel crash, root cause, and exploitability are not claimed.</p>
                <section class="grid">
                  <div class="metric"><strong>{{result.Executions}}</strong>executions</div>
                  <div class="metric"><strong>{{result.Corpus.Count}}</strong>corpus cases</div>
                  <div class="metric"><strong>{{result.GlobalCoverage.Count}}</strong>coverage elements</div>
                  <div class="metric"><strong>{{result.Findings.Sum(static finding => finding.Occurrences)}}</strong>raw synthetic failures</div>
                  <div class="metric"><strong>{{result.Findings.Count}}</strong>exact signatures</div>
                </section>
                <h2>First finding</h2>
                <p><code>{{firstFindingText}}</code></p>
                <h2>Reproducibility inputs</h2>
                <p>Strategy: <code>{{result.Strategy}}</code><br>Campaign seed: <code>{{result.CampaignSeed}}</code><br>Seed case: <code>{{result.SeedCaseId}}</code></p>
              </main>
            </body>
            </html>
            """);
    }

    private static string Csv(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static Task WriteJsonAsync(string path, object value, CancellationToken cancellationToken) =>
        WriteBytesAsync(path, ArtifactText.SerializeJson(value, ContractJson.Indented), cancellationToken);

    private static async Task WriteBytesAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Output path has no parent."));
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                65_536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
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
