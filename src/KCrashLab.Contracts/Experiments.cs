namespace KCrashLab.Contracts;

public sealed record E1TrialResult(
    int Trial,
    string Strategy,
    long CampaignSeed,
    int Executions,
    bool Found,
    int? FirstFindingExecution,
    int CoverageCount,
    int CorpusCount,
    int RawSyntheticFailures,
    int ExactSignatures);

public sealed record E1StrategySummary(
    string Strategy,
    int Trials,
    int Discoveries,
    int CensoredTrials,
    double DiscoveryRate,
    double? MedianFirstFindingAmongDiscoveries,
    double? FirstFindingQ1AmongDiscoveries,
    double? FirstFindingQ3AmongDiscoveries);

public sealed record E1SurvivalPoint(
    string Strategy,
    int Execution,
    int AtRisk,
    int Discoveries,
    int Censored,
    double SurvivalProbability,
    double CumulativeDiscoveryProbability);

public sealed record E1FactorialContrast(
    string Contrast,
    string LeftStrategy,
    string RightStrategy,
    int BothDiscovered,
    int LeftOnly,
    int RightOnly,
    int NeitherDiscovered);

public sealed record E1ExperimentResult(
    int SchemaVersion,
    string ExecutionMode,
    string Experiment,
    string SeedCaseId,
    int BudgetPerTrial,
    int TrialsPerStrategy,
    long BaseCampaignSeed,
    IReadOnlyList<E1TrialResult> Trials,
    IReadOnlyList<E1StrategySummary> Strategies,
    IReadOnlyList<E1SurvivalPoint> SurvivalCurve,
    IReadOnlyList<E1FactorialContrast> FactorialContrasts);

public sealed record E2TrialResult(
    int Trial,
    string Mode,
    int MaximumSequenceLength,
    long CampaignSeed,
    int Executions,
    bool Found,
    int? FirstFindingExecution,
    int CoverageCount,
    int CorpusCount,
    int RawSyntheticFailures,
    int ExactSignatures);

public sealed record E2ModeSummary(
    string Mode,
    int MaximumSequenceLength,
    int Trials,
    int Discoveries,
    int CensoredTrials,
    double DiscoveryRate,
    double? MedianFirstFindingAmongDiscoveries,
    double? FirstFindingQ1AmongDiscoveries,
    double? FirstFindingQ3AmongDiscoveries);

public sealed record E2PairedOutcomeSummary(
    int BothDiscovered,
    int StatefulOnly,
    int SingleCallOnly,
    int NeitherDiscovered);

public sealed record E2ExperimentResult(
    int SchemaVersion,
    string ExecutionMode,
    string Experiment,
    string SeedCaseId,
    int BudgetPerTrial,
    int TrialsPerMode,
    long BaseCampaignSeed,
    int SingleCallMaximumSequenceLength,
    int StatefulMaximumSequenceLength,
    IReadOnlyList<E2TrialResult> Trials,
    IReadOnlyList<E2ModeSummary> Modes,
    E2PairedOutcomeSummary PairedOutcomes);
