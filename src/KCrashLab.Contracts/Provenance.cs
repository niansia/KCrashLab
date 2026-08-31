namespace KCrashLab.Contracts;

public sealed record ExperimentProvenance(
    string RecordedAtUtc,
    string SourceCommitTimeUtc,
    string ReproducibleTimestampPolicy,
    string GitCommit,
    string SourceTreeDigest,
    string ExperimentDefinitionDigest,
    int CaseSchemaVersion,
    string EngineVersion);

public sealed record MinimizationReplayProvenance(
    string RecordedAtUtc,
    string SourceCommitTimeUtc,
    string ReproducibleTimestampPolicy,
    string GitCommit,
    string SourceTreeDigest,
    string ExperimentDefinitionDigest,
    int CaseSchemaVersion,
    string EngineVersion,
    string Scenario,
    long CampaignSeed,
    int MaximumOracleAttempts,
    string MinimizerDefinitionDigest,
    string ReplayPolicyDefinitionDigest);
