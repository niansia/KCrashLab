namespace KCrashLab.Contracts;

public enum ParseConfidence
{
    None,
    Low,
    Medium,
    High
}

public sealed record TriageAnalysis(
    int SchemaVersion,
    string ExecutionMode,
    string FixtureVersion,
    string BugcheckCode,
    IReadOnlyList<string> RelevantParameters,
    string FaultingModule,
    string? VerifierRuleId,
    IReadOnlyList<string> NormalizedFrames,
    ParseConfidence ParseConfidence,
    IReadOnlyList<string> Warnings);

public sealed record ReplayPolicy(int Attempts, int RequiredMatches);

public enum ReplayAttemptClass
{
    Match,
    Divergent,
    NoFailure,
    InfrastructureError
}

public sealed record ReplayAttempt(
    int Attempt,
    ReplayAttemptClass Classification,
    string? ObservedSignature);

public sealed record ReplayDecision(
    ReplayPolicy Policy,
    IReadOnlyList<ReplayAttempt> Attempts,
    int EligibleAttempts,
    int MatchingAttempts,
    bool Passed);

