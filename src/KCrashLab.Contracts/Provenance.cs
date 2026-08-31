namespace KCrashLab.Contracts;

public sealed record ExperimentProvenance(
    string RecordedAtUtc,
    string GitCommit,
    string SourceTreeDigest,
    string ExperimentDefinitionDigest,
    int CaseSchemaVersion,
    string EngineVersion);
