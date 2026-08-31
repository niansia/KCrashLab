using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Controller;

public static class E2ExperimentRunner
{
    public const int SingleCallMaximumSequenceLength = 1;
    public const int StatefulMaximumSequenceLength = 6;

    public static async Task<E2ExperimentResult> RunAsync(
        CanonicalCase seed,
        int budgetPerTrial,
        int trialsPerMode,
        long baseCampaignSeed,
        Func<CanonicalCase, CancellationToken, Task<FuzzObservation>> evaluator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentOutOfRangeException.ThrowIfLessThan(budgetPerTrial, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(trialsPerMode, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(baseCampaignSeed);
        if (seed.Value.Operations.Count > SingleCallMaximumSequenceLength)
        {
            throw new InvalidDataException("E2 seed must fit the single-call sequence limit.");
        }

        if (baseCampaignSeed > long.MaxValue - trialsPerMode + 1L)
        {
            throw new ArgumentOutOfRangeException(nameof(baseCampaignSeed), "Trial seeds would overflow Int64.");
        }

        var results = new List<E2TrialResult>(trialsPerMode * 2);
        for (var trial = 1; trial <= trialsPerMode; trial++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var campaignSeed = baseCampaignSeed + trial - 1L;
            results.Add(ToTrial(
                trial,
                "SINGLE_CALL",
                SingleCallMaximumSequenceLength,
                await RunModeAsync(seed, budgetPerTrial, campaignSeed, SingleCallMaximumSequenceLength, evaluator, cancellationToken).ConfigureAwait(false)));
            results.Add(ToTrial(
                trial,
                "STATEFUL",
                StatefulMaximumSequenceLength,
                await RunModeAsync(seed, budgetPerTrial, campaignSeed, StatefulMaximumSequenceLength, evaluator, cancellationToken).ConfigureAwait(false)));
        }

        var ordered = results.OrderBy(static result => result.Trial).ThenBy(static result => result.Mode, StringComparer.Ordinal).ToArray();
        var summaries = ordered
            .GroupBy(static result => result.Mode, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => Summarize(group.Key, group.ToArray()))
            .ToArray();
        return new E2ExperimentResult(
            1,
            "SIMULATED",
            "E2_STATEFUL_VS_SINGLE_CALL_V1",
            seed.CaseId,
            budgetPerTrial,
            trialsPerMode,
            baseCampaignSeed,
            SingleCallMaximumSequenceLength,
            StatefulMaximumSequenceLength,
            ordered,
            summaries,
            BuildPairedOutcomes(ordered));
    }

    public static E2PairedOutcomeSummary BuildPairedOutcomes(IReadOnlyList<E2TrialResult> trials)
    {
        var both = 0;
        var statefulOnly = 0;
        var singleCallOnly = 0;
        var neither = 0;
        foreach (var pair in trials.GroupBy(static trial => trial.Trial).OrderBy(static pair => pair.Key))
        {
            var singleCall = pair.Single(static trial => trial.Mode == "SINGLE_CALL").Found;
            var stateful = pair.Single(static trial => trial.Mode == "STATEFUL").Found;
            if (singleCall && stateful)
            {
                both++;
            }
            else if (stateful)
            {
                statefulOnly++;
            }
            else if (singleCall)
            {
                singleCallOnly++;
            }
            else
            {
                neither++;
            }
        }

        return new E2PairedOutcomeSummary(both, statefulOnly, singleCallOnly, neither);
    }

    private static Task<FuzzCampaignResult> RunModeAsync(
        CanonicalCase seed,
        int budget,
        long campaignSeed,
        int maximumSequenceLength,
        Func<CanonicalCase, CancellationToken, Task<FuzzObservation>> evaluator,
        CancellationToken cancellationToken)
    {
        var operators = DefaultMutationOperators.Create()
            .Select(item => (ICaseMutationOperator)new SequenceLengthCappedMutationOperator(item, maximumSequenceLength))
            .ToArray();
        return new DeterministicFuzzEngine(operators).RunAsync(seed, budget, campaignSeed, evaluator, cancellationToken);
    }

    private static E2TrialResult ToTrial(int trial, string mode, int maximumSequenceLength, FuzzCampaignResult result)
    {
        var firstFinding = result.Findings.Count == 0 ? null : result.Findings[0];
        return new E2TrialResult(
            trial,
            mode,
            maximumSequenceLength,
            result.CampaignSeed,
            result.Executions,
            firstFinding is not null,
            firstFinding?.FirstExecution,
            result.GlobalCoverage.Count,
            result.Corpus.Count,
            result.Findings.Sum(static finding => finding.Occurrences),
            result.Findings.Count);
    }

    private static E2ModeSummary Summarize(string mode, E2TrialResult[] trials)
    {
        var firstFindings = trials
            .Where(static trial => trial.FirstFindingExecution.HasValue)
            .Select(static trial => trial.FirstFindingExecution!.Value)
            .Order()
            .ToArray();
        return new E2ModeSummary(
            mode,
            trials[0].MaximumSequenceLength,
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
