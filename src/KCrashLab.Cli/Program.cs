using System.Text.Json;
using KCrashLab.Contracts;
using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

return await KCrashCli.RunAsync(args, CancellationToken.None).ConfigureAwait(false);

internal static class KCrashCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            if (args is ["lab", "probe", ..])
            {
                return await ProbeAsync(args[2..], cancellationToken).ConfigureAwait(false);
            }

            if (args is ["case", "id", var casePath])
            {
                var canonical = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(casePath, cancellationToken).ConfigureAwait(false));
                Console.WriteLine(canonical.CaseId);
                return 0;
            }

            if (args is ["campaign", "run", ..])
            {
                return await RunCampaignAsync(args[2..], cancellationToken).ConfigureAwait(false);
            }

            if (args is ["fuzz", "run", ..])
            {
                return await RunFuzzCampaignAsync(args[2..], cancellationToken).ConfigureAwait(false);
            }

            if (args is ["fuzz", "verify", var fuzzRoot])
            {
                return await VerifyFuzzCampaignAsync(fuzzRoot, cancellationToken).ConfigureAwait(false);
            }

            if (args is ["experiment", "e1", ..])
            {
                return await RunE1ExperimentAsync(args[2..], cancellationToken).ConfigureAwait(false);
            }

            if (args is ["experiment", "e2", ..])
            {
                return await RunE2ExperimentAsync(args[2..], cancellationToken).ConfigureAwait(false);
            }

            if (args is ["experiment", "verify", var experimentRoot])
            {
                return await VerifyExperimentAsync(experimentRoot, cancellationToken).ConfigureAwait(false);
            }

            if (args is ["evidence", "verify", var bundle])
            {
                var result = await EvidenceBundleVerifier.VerifyAsync(bundle, cancellationToken).ConfigureAwait(false);
                Console.WriteLine(result.IsValid
                    ? $"VERIFIED: {result.VerifiedFiles} files; simulation claims policy passed."
                    : $"FAILED: {result.Errors.Count} error(s).");
                foreach (var error in result.Errors)
                {
                    Console.Error.WriteLine($"- {error}");
                }

                return result.IsValid ? 0 : 2;
            }

            PrintHelp();
            return args.Length == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 2;
        }
    }

    private static async Task<int> ProbeAsync(string[] args, CancellationToken cancellationToken)
    {
        var output = GetRequiredOption(args, "--output");
        var report = HostCapabilityProbe.Probe(DateTimeOffset.UtcNow);
        var path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Probe output path has no parent."));
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(report, ContractJson.Indented), cancellationToken).ConfigureAwait(false);

        Console.WriteLine("Capability report written.");
        Console.WriteLine($"  Hypervisor:          {report.Capabilities.Hypervisor.ToString().ToUpperInvariant()}");
        Console.WriteLine($"  Hyper-V management: {report.Capabilities.HypervManagement.ToString().ToUpperInvariant()}");
        Console.WriteLine($"  Windows SDK:        {report.Capabilities.WindowsSdk.ToString().ToUpperInvariant()}");
        Console.WriteLine($"  WDK targets:        {report.Capabilities.WdkDriverTargets.ToString().ToUpperInvariant()}");
        Console.WriteLine("  Real kernel campaign: BLOCKED");
        return 0;
    }

    private static async Task<int> RunCampaignAsync(string[] args, CancellationToken cancellationToken)
    {
        var scenarioName = GetRequiredOption(args, "--scenario");
        var casePath = GetRequiredOption(args, "--case");
        var output = Path.GetFullPath(GetRequiredOption(args, "--output"));
        var scenarioPath = GetOption(args, "--scenario-file")
            ?? Path.Combine(Environment.CurrentDirectory, "samples", "scenarios", scenarioName + ".json");

        var fixture = await ScenarioFixtureLoader.LoadAsync(scenarioPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(fixture.Name, scenarioName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Scenario name does not match fixture name.");
        }

        var canonical = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(casePath, cancellationToken).ConfigureAwait(false));
        var campaignId = DeterministicIdentity.CreateGuid("campaign", fixture.Name, canonical.CaseId, fixture.Seed);
        var store = new SqliteCampaignEventStore(Path.Combine(output, ".journal", "campaign.db"));
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var backend = new SimulatedLabBackend(fixture);
        var orchestrator = new CampaignOrchestrator(store, backend);
        var result = await orchestrator.RunAsync(
            new CampaignSpec(campaignId, fixture.Name, fixture.Seed),
            canonical,
            stopAfterState: null,
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Campaign {campaignId:D}: {result.State.ToString().ToUpperInvariant()} ({result.ResultClass})");
        if (result.State != CampaignState.Complete || result.Signature is null)
        {
            Console.WriteLine("No finding bundle was produced.");
            return result.State is CampaignState.Quarantined or CampaignState.InfraFailed ? 2 : 0;
        }

        var originalSignature = SyntheticStateTarget.Evaluate(canonical);
        if (!string.Equals(originalSignature, result.Signature, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected case does not reproduce the fixture's synthetic target signature.");
        }

        var minimization = await HierarchicalMinimizer.MinimizeAsync(
            canonical,
            result.Signature,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Evaluate(candidate)),
            maximumAttempts: 128,
            cancellationToken).ConfigureAwait(false);
        var replay = await ReplayEngine.RunAsync(
            new ReplayPolicy(3, 3),
            (_, _) => Task.FromResult(SyntheticStateTarget.Evaluate(minimization.Minimized)),
            result.Signature,
            cancellationToken).ConfigureAwait(false);

        var bundle = Path.Combine(output, "finding");
        var built = await EvidenceBundleBuilder.BuildAsync(bundle, result, canonical, minimization, replay, cancellationToken).ConfigureAwait(false);
        var verification = await EvidenceBundleVerifier.VerifyAsync(bundle, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Minimized: {canonical.Value.Operations.Count} -> {minimization.Minimized.Value.Operations.Count} operations");
        Console.WriteLine($"Bytes: {canonical.CanonicalUtf8.Length} -> {minimization.Minimized.CanonicalUtf8.Length}");
        Console.WriteLine($"Replay: {replay.MatchingAttempts}/{replay.EligibleAttempts} matching");
        Console.WriteLine($"Evidence: {(verification.IsValid ? "VERIFIED" : "FAILED")} ({built.ManifestEntries} manifest entries)");
        Console.WriteLine($"Bundle: {built.BundleRoot}");
        if (!verification.IsValid)
        {
            foreach (var error in verification.Errors)
            {
                Console.Error.WriteLine($"- {error}");
            }
        }

        return verification.IsValid ? 0 : 2;
    }

    private static async Task<int> RunFuzzCampaignAsync(string[] args, CancellationToken cancellationToken)
    {
        var seedPath = GetRequiredOption(args, "--seed");
        var output = Path.GetFullPath(GetRequiredOption(args, "--output"));
        var budget = GetIntOption(args, "--budget", 256);
        var campaignSeed = GetLongOption(args, "--campaign-seed", 20260831);
        var strategy = GetOption(args, "--strategy") ?? "novelty";
        if (budget is < 1 or > 1_000_000)
        {
            throw new InvalidDataException("--budget must be between 1 and 1,000,000.");
        }

        if (campaignSeed < 0)
        {
            throw new InvalidDataException("--campaign-seed must be non-negative.");
        }

        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(seedPath, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(seed.Value.Target, "kcl.state", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The current simulated fuzz target requires a kcl.state seed case.");
        }

        if (SyntheticStateTarget.Evaluate(seed) is not null)
        {
            throw new InvalidDataException("The fuzz seed must be a non-failing case.");
        }

        var result = strategy switch
        {
            "novelty" => await new DeterministicFuzzEngine(DefaultMutationOperators.Create()).RunAsync(
                seed,
                budget,
                campaignSeed,
                static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
                cancellationToken).ConfigureAwait(false),
            "random" => await new UniformRandomFuzzEngine(DefaultMutationOperators.Create()).RunAsync(
                seed,
                budget,
                campaignSeed,
                static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidDataException("--strategy must be novelty or random.")
        };
        var provenance = await ExperimentProvenanceBuilder.ForFuzzAsync(
            Environment.CurrentDirectory,
            result,
            GetOption(args, "--recorded-at") ?? ExperimentProvenanceBuilder.Unspecified,
            GetOption(args, "--git-commit") ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? ExperimentProvenanceBuilder.Uncommitted,
            cancellationToken).ConfigureAwait(false);
        var built = await FuzzCampaignArtifacts.BuildAsync(output, result, provenance, cancellationToken).ConfigureAwait(false);
        var verification = await FuzzCampaignArtifacts.VerifyAsync(output, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Fuzz campaign: {result.Executions}/{result.Budget} executions (SIMULATED)");
        Console.WriteLine($"Coverage: {result.GlobalCoverage.Count} elements; corpus: {result.Corpus.Count} cases");
        Console.WriteLine($"Synthetic failures: {result.Findings.Sum(static finding => finding.Occurrences)} raw; {result.Findings.Count} exact signature(s)");
        if (result.Findings.Count > 0)
        {
            var first = result.Findings[0];
            Console.WriteLine($"First finding: execution {first.FirstExecution}; {first.Signature}");
        }

        Console.WriteLine($"Artifacts: {(verification.IsValid ? "VERIFIED" : "FAILED")} ({built.ManifestEntries} manifest entries)");
        Console.WriteLine($"Output: {built.Root}");
        if (!verification.IsValid)
        {
            foreach (var error in verification.Errors)
            {
                Console.Error.WriteLine($"- {error}");
            }
        }

        return verification.IsValid ? 0 : 2;
    }

    private static async Task<int> VerifyFuzzCampaignAsync(string root, CancellationToken cancellationToken)
    {
        var verification = await FuzzCampaignArtifacts.VerifyAsync(root, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(verification.IsValid
            ? $"VERIFIED: {verification.VerifiedFiles} files; simulated fuzz claims policy passed."
            : $"FAILED: {verification.Errors.Count} error(s).");
        foreach (var error in verification.Errors)
        {
            Console.Error.WriteLine($"- {error}");
        }

        return verification.IsValid ? 0 : 2;
    }

    private static async Task<int> RunE1ExperimentAsync(string[] args, CancellationToken cancellationToken)
    {
        var seedPath = GetRequiredOption(args, "--seed");
        var output = Path.GetFullPath(GetRequiredOption(args, "--output"));
        var budget = GetIntOption(args, "--budget", 256);
        var trials = GetIntOption(args, "--trials", 20);
        var baseCampaignSeed = GetLongOption(args, "--base-seed", 20260831);
        if (budget is < 1 or > 1_000_000)
        {
            throw new InvalidDataException("--budget must be between 1 and 1,000,000.");
        }

        if (trials is < 1 or > 1_000)
        {
            throw new InvalidDataException("--trials must be between 1 and 1,000.");
        }

        if (baseCampaignSeed < 0 || baseCampaignSeed > long.MaxValue - trials + 1L)
        {
            throw new InvalidDataException("--base-seed is outside the valid paired-trial range.");
        }

        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(seedPath, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(seed.Value.Target, "kcl.state", StringComparison.Ordinal)
            || SyntheticStateTarget.Evaluate(seed) is not null)
        {
            throw new InvalidDataException("E1 currently requires a non-failing kcl.state seed case.");
        }

        var result = await E1ExperimentRunner.RunAsync(
            seed,
            budget,
            trials,
            baseCampaignSeed,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            cancellationToken).ConfigureAwait(false);
        var provenance = await ExperimentProvenanceBuilder.ForE1Async(
            Environment.CurrentDirectory,
            result,
            GetOption(args, "--recorded-at") ?? ExperimentProvenanceBuilder.Unspecified,
            GetOption(args, "--git-commit") ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? ExperimentProvenanceBuilder.Uncommitted,
            cancellationToken).ConfigureAwait(false);
        var built = await E1ExperimentArtifacts.BuildAsync(output, result, provenance, cancellationToken).ConfigureAwait(false);
        var verification = await E1ExperimentArtifacts.VerifyAsync(output, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"E1 paired 2x2 policy ablation: {trials} trial(s) per arm; {budget} executions per trial (SIMULATED)");
        foreach (var summary in result.Strategies)
        {
            var median = summary.MedianFirstFindingAmongDiscoveries?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
            var q1 = summary.FirstFindingQ1AmongDiscoveries?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
            var q3 = summary.FirstFindingQ3AmongDiscoveries?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
            Console.WriteLine($"{summary.Strategy}: {summary.Discoveries}/{summary.Trials} discoveries; {summary.CensoredTrials} censored; successful-trial median [Q1, Q3] {median} [{q1}, {q3}]");
        }

        Console.WriteLine($"Artifacts: {(verification.IsValid ? "VERIFIED" : "FAILED")} ({built.ManifestEntries} manifest entries)");
        Console.WriteLine($"Output: {built.Root}");
        if (!verification.IsValid)
        {
            foreach (var error in verification.Errors)
            {
                Console.Error.WriteLine($"- {error}");
            }
        }

        return verification.IsValid ? 0 : 2;
    }

    private static async Task<int> RunE2ExperimentAsync(string[] args, CancellationToken cancellationToken)
    {
        var seedPath = GetRequiredOption(args, "--seed");
        var output = Path.GetFullPath(GetRequiredOption(args, "--output"));
        var budget = GetIntOption(args, "--budget", 512);
        var trials = GetIntOption(args, "--trials", 20);
        var baseCampaignSeed = GetLongOption(args, "--base-seed", 20260831);
        if (budget is < 1 or > 1_000_000 || trials is < 1 or > 1_000)
        {
            throw new InvalidDataException("E2 budget must be 1..1,000,000 and trials must be 1..1,000.");
        }

        if (baseCampaignSeed < 0 || baseCampaignSeed > long.MaxValue - trials + 1L)
        {
            throw new InvalidDataException("--base-seed is outside the valid paired-trial range.");
        }

        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(seedPath, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(seed.Value.Target, "kcl.state", StringComparison.Ordinal)
            || seed.Value.Operations.Count > 1
            || SyntheticStateTarget.Evaluate(seed) is not null)
        {
            throw new InvalidDataException("E2 requires a non-failing kcl.state seed containing at most one operation.");
        }

        var result = await E2ExperimentRunner.RunAsync(
            seed,
            budget,
            trials,
            baseCampaignSeed,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
            cancellationToken).ConfigureAwait(false);
        var provenance = await ExperimentProvenanceBuilder.ForE2Async(
            Environment.CurrentDirectory,
            result,
            GetOption(args, "--recorded-at") ?? ExperimentProvenanceBuilder.Unspecified,
            GetOption(args, "--git-commit") ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? ExperimentProvenanceBuilder.Uncommitted,
            cancellationToken).ConfigureAwait(false);
        var built = await E2ExperimentArtifacts.BuildAsync(output, result, provenance, cancellationToken).ConfigureAwait(false);
        var verification = await E2ExperimentArtifacts.VerifyAsync(output, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"E2 paired experiment: {trials} trial(s) per mode; budget {budget} (SIMULATED)");
        foreach (var summary in result.Modes)
        {
            Console.WriteLine($"{summary.Mode} (max {summary.MaximumSequenceLength}): {summary.Discoveries}/{summary.Trials} discoveries; {summary.CensoredTrials} censored");
        }

        Console.WriteLine($"Paired: {result.PairedOutcomes.StatefulOnly} stateful-only; {result.PairedOutcomes.SingleCallOnly} single-call-only");
        Console.WriteLine($"Artifacts: {(verification.IsValid ? "VERIFIED" : "FAILED")} ({built.ManifestEntries} manifest entries)");
        Console.WriteLine($"Output: {built.Root}");
        if (!verification.IsValid)
        {
            foreach (var error in verification.Errors)
            {
                Console.Error.WriteLine($"- {error}");
            }
        }

        return verification.IsValid ? 0 : 2;
    }

    private static async Task<int> VerifyExperimentAsync(string root, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json"), cancellationToken).ConfigureAwait(false));
        var experiment = document.RootElement.GetProperty("experiment").GetString();
        if (experiment == "E1_POLICY_ABLATION_2X2_V2")
        {
            var verification = await E1ExperimentArtifacts.VerifyAsync(root, cancellationToken).ConfigureAwait(false);
            PrintExperimentVerification("E1", verification.IsValid, verification.VerifiedFiles, verification.Errors);
            return verification.IsValid ? 0 : 2;
        }

        if (experiment == "E2_STATEFUL_VS_SINGLE_CALL_V1")
        {
            var verification = await E2ExperimentArtifacts.VerifyAsync(root, cancellationToken).ConfigureAwait(false);
            PrintExperimentVerification("E2", verification.IsValid, verification.VerifiedFiles, verification.Errors);
            return verification.IsValid ? 0 : 2;
        }

        throw new InvalidDataException("Unsupported experiment artifact type.");
    }

    private static void PrintExperimentVerification(string experiment, bool isValid, int verifiedFiles, IReadOnlyList<string> errors)
    {
        Console.WriteLine(isValid
            ? $"VERIFIED: {verifiedFiles} files; simulated {experiment} claims policy passed."
            : $"FAILED: {errors.Count} error(s).");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"- {error}");
        }
    }

    private static string GetRequiredOption(string[] args, string name) =>
        GetOption(args, name) ?? throw new InvalidDataException($"Missing required option {name}.");

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == name && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int GetIntOption(string[] args, string name, int defaultValue)
    {
        var value = GetOption(args, name);
        return value is null
            ? defaultValue
            : int.TryParse(value, out var parsed)
                ? parsed
                : throw new InvalidDataException($"Option {name} must be an integer.");
    }

    private static long GetLongOption(string[] args, string name, long defaultValue)
    {
        var value = GetOption(args, name);
        return value is null
            ? defaultValue
            : long.TryParse(value, out var parsed)
                ? parsed
                : throw new InvalidDataException($"Option {name} must be an integer.");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            KCrashLab Control Plane & Reproducibility Core
            SIMULATION ONLY — NOT A REAL KERNEL FUZZER

            Commands:
              kcrash lab probe --output <capability-report.json>
              kcrash case id <case.json>
              kcrash campaign run --scenario <name> --case <case.json> --output <directory>
              kcrash evidence verify <bundle-directory>
              kcrash fuzz run --seed <safe.case.json> --strategy <novelty|random> --budget <executions> --campaign-seed <integer> --recorded-at <UTC|UNSPECIFIED> --output <directory>
                novelty = novelty-only corpus admission + energy parent selection
                random  = keep-all corpus admission + uniform parent selection
              kcrash fuzz verify <campaign-directory>
              kcrash experiment e1 --seed <safe.case.json> --budget <executions> --trials <count> --base-seed <integer> --recorded-at <UTC|UNSPECIFIED> --output <directory>
              kcrash experiment e2 --seed <single-call.case.json> --budget <executions> --trials <count> --base-seed <integer> --recorded-at <UTC|UNSPECIFIED> --output <directory>
              kcrash experiment verify <experiment-directory>
            """);
    }
}
