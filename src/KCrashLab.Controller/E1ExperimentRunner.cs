using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Controller;

public static class E1ExperimentRunner
{
    public const string KeepAllUniform = "KEEP_ALL_UNIFORM_V2";
    public const string KeepAllEnergyRanked = "KEEP_ALL_ENERGY_RANKED_V2";
    public const string NoveltyUniform = "NOVELTY_ONLY_UNIFORM_V2";
    public const string NoveltyEnergyRanked = "NOVELTY_ONLY_ENERGY_RANKED_V2";

    public static IReadOnlySet<string> Strategies { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        KeepAllUniform,
        KeepAllEnergyRanked,
        NoveltyUniform,
        NoveltyEnergyRanked
    };

    public static async Task<E1ExperimentResult> RunAsync(
        CanonicalCase seed,
        int budgetPerTrial,
        int trialsPerStrategy,
        long baseCampaignSeed,
        Func<CanonicalCase, CancellationToken, Task<FuzzObservation>> evaluator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentOutOfRangeException.ThrowIfLessThan(budgetPerTrial, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(trialsPerStrategy, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(baseCampaignSeed);
        if (baseCampaignSeed > long.MaxValue - trialsPerStrategy + 1L)
        {
            throw new ArgumentOutOfRangeException(nameof(baseCampaignSeed), "Trial seeds would overflow Int64.");
        }

        var policySets = new[]
        {
            FuzzPolicySet.KeepAllUniform(),
            FuzzPolicySet.KeepAllEnergyRanked(),
            FuzzPolicySet.NoveltyUniform(),
            FuzzPolicySet.NoveltyEnergyRanked()
        };
        var results = new List<E1TrialResult>(trialsPerStrategy * policySets.Length);
        for (var trial = 1; trial <= trialsPerStrategy; trial++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var campaignSeed = baseCampaignSeed + trial - 1L;
            foreach (var policySet in policySets)
            {
                var campaign = await new PolicyDrivenFuzzEngine(DefaultMutationOperators.Create(), policySet).RunAsync(
                    seed,
                    budgetPerTrial,
                    campaignSeed,
                    evaluator,
                    cancellationToken).ConfigureAwait(false);
                results.Add(ToTrial(trial, campaign));
            }
        }

        var ordered = results
            .OrderBy(static result => result.Trial)
            .ThenBy(static result => result.Strategy, StringComparer.Ordinal)
            .ToArray();
        var summaries = ordered
            .GroupBy(static result => result.Strategy, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => Summarize(group.Key, group.ToArray()))
            .ToArray();
        return new E1ExperimentResult(
            2,
            "SIMULATED",
            "E1_POLICY_ABLATION_2X2_V2",
            seed.CaseId,
            budgetPerTrial,
            trialsPerStrategy,
            baseCampaignSeed,
            ordered,
            summaries,
            BuildSurvivalCurve(ordered, budgetPerTrial),
            BuildFactorialContrasts(ordered));
    }

    public static IReadOnlyList<E1SurvivalPoint> BuildSurvivalCurve(
        IReadOnlyList<E1TrialResult> trials,
        int budgetPerTrial)
    {
        ArgumentNullException.ThrowIfNull(trials);
        ArgumentOutOfRangeException.ThrowIfLessThan(budgetPerTrial, 1);
        var points = new List<E1SurvivalPoint>();
        foreach (var strategyGroup in trials
                     .GroupBy(static trial => trial.Strategy, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var strategyTrials = strategyGroup.ToArray();
            if (strategyTrials.Any(trial =>
                    trial.Executions < 1
                    || trial.Executions > budgetPerTrial
                    || trial.Found != trial.FirstFindingExecution.HasValue
                    || (trial.FirstFindingExecution.HasValue
                        && (trial.FirstFindingExecution.Value < 1 || trial.FirstFindingExecution.Value > trial.Executions))))
            {
                throw new InvalidDataException("E1 trial outcome is inconsistent with its execution bounds.");
            }

            var atRisk = strategyTrials.Length;
            var survival = 1d;
            points.Add(new E1SurvivalPoint(strategyGroup.Key, 0, atRisk, 0, 0, survival, 0));
            var eventTimes = strategyTrials
                .Select(static trial => trial.FirstFindingExecution ?? trial.Executions)
                .Distinct()
                .Order()
                .ToArray();
            foreach (var execution in eventTimes)
            {
                var discoveries = strategyTrials.Count(trial => trial.FirstFindingExecution == execution);
                var censored = strategyTrials.Count(trial => !trial.Found && trial.Executions == execution);
                if (discoveries > 0)
                {
                    survival *= 1d - ((double)discoveries / atRisk);
                }

                points.Add(new E1SurvivalPoint(
                    strategyGroup.Key,
                    execution,
                    atRisk,
                    discoveries,
                    censored,
                    survival,
                    1d - survival));
                atRisk -= discoveries + censored;
            }
        }

        return points;
    }

    public static IReadOnlyList<E1FactorialContrast> BuildFactorialContrasts(IReadOnlyList<E1TrialResult> trials)
    {
        return new[]
        {
            BuildContrast("ADMISSION_AT_UNIFORM_PARENT", NoveltyUniform, KeepAllUniform, trials),
            BuildContrast("ADMISSION_AT_ENERGY_RANKED_PARENT", NoveltyEnergyRanked, KeepAllEnergyRanked, trials),
            BuildContrast("PARENT_AT_KEEP_ALL_ADMISSION", KeepAllEnergyRanked, KeepAllUniform, trials),
            BuildContrast("PARENT_AT_NOVELTY_ADMISSION", NoveltyEnergyRanked, NoveltyUniform, trials)
        };
    }

    private static E1FactorialContrast BuildContrast(
        string contrast,
        string leftStrategy,
        string rightStrategy,
        IReadOnlyList<E1TrialResult> trials)
    {
        var both = 0;
        var leftOnly = 0;
        var rightOnly = 0;
        var neither = 0;
        foreach (var pair in trials.GroupBy(static trial => trial.Trial).OrderBy(static pair => pair.Key))
        {
            var left = pair.Single(trial => trial.Strategy == leftStrategy).Found;
            var right = pair.Single(trial => trial.Strategy == rightStrategy).Found;
            if (left && right)
            {
                both++;
            }
            else if (left)
            {
                leftOnly++;
            }
            else if (right)
            {
                rightOnly++;
            }
            else
            {
                neither++;
            }
        }

        return new E1FactorialContrast(
            contrast,
            leftStrategy,
            rightStrategy,
            both,
            leftOnly,
            rightOnly,
            neither);
    }

    private static E1TrialResult ToTrial(int trial, FuzzCampaignResult result)
    {
        var firstFinding = result.Findings.Count == 0 ? null : result.Findings[0];
        return new E1TrialResult(
            trial,
            result.Strategy,
            result.CampaignSeed,
            result.Executions,
            firstFinding is not null,
            firstFinding?.FirstExecution,
            result.GlobalCoverage.Count,
            result.Corpus.Count,
            result.Findings.Sum(static finding => finding.Occurrences),
            result.Findings.Count);
    }

    private static E1StrategySummary Summarize(string strategy, E1TrialResult[] trials)
    {
        var firstFindings = trials
            .Where(static trial => trial.FirstFindingExecution.HasValue)
            .Select(static trial => trial.FirstFindingExecution!.Value)
            .Order()
            .ToArray();
        return new E1StrategySummary(
            strategy,
            trials.Length,
            firstFindings.Length,
            trials.Length - firstFindings.Length,
            (double)firstFindings.Length / trials.Length,
            Quantile(firstFindings, 0.5),
            Quantile(firstFindings, 0.25),
            Quantile(firstFindings, 0.75));
    }

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
}
