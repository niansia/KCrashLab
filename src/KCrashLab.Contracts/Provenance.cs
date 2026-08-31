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
