namespace KCrashLab.Contracts;

public sealed record FuzzObservation(
    string ResultClass,
    IReadOnlyList<string> Coverage,
    string? Signature);

public sealed record FuzzExecutionRecord(
    int Execution,
    string CaseId,
    string? ParentCaseId,
    string OperatorId,
    int NovelCoverage,
    bool AddedToCorpus,
    string ResultClass,
    string? Signature);

public sealed record CorpusEntrySnapshot(
    CanonicalCase TestCase,
    int DiscoveryExecution,
    int NovelCoverage,
    int Energy,
    int Selections,
    IReadOnlyList<string> Coverage);

public sealed record FuzzFindingSnapshot(
    string Signature,
    string FirstCaseId,
    int FirstExecution,
    int Occurrences,
    CanonicalCase Representative);

public sealed record FuzzCampaignResult(
    int SchemaVersion,
    string ExecutionMode,
    string Strategy,
    long CampaignSeed,
    int Budget,
    int Executions,
    string TerminationReason,
    int SchedulingIterations,
    int SchedulingLimit,
    int DuplicateCandidateSkips,
    int EmptyCandidatePolls,
    string CandidateEnumeration,
    int MaximumCandidatesPerOperator,
    string SeedCaseId,
    IReadOnlyList<string> GlobalCoverage,
    IReadOnlyList<CorpusEntrySnapshot> Corpus,
    IReadOnlyList<FuzzFindingSnapshot> Findings,
    IReadOnlyList<FuzzExecutionRecord> ExecutionLog);
