using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class ReplayEngineTests
{
    [Fact]
    public async Task InfrastructureErrorsAreRecordedButExcludedFromVoting()
    {
        var decision = await ReplayEngine.RunAsync(
            new ReplayPolicy(5, 3),
            static (attempt, _) => attempt <= 2
                ? Task.FromException<string?>(new IOException("simulated reset failure"))
                : Task.FromResult<string?>("target"),
            "target",
            CancellationToken.None);

        Assert.True(decision.Passed);
        Assert.Equal(3, decision.EligibleAttempts);
        Assert.Equal(3, decision.MatchingAttempts);
        Assert.Equal(2, decision.Attempts.Count(static attempt => attempt.Classification == ReplayAttemptClass.InfrastructureError));
    }

    [Fact]
    public async Task VotingOracleRejectsFlakyCandidateBelowThreshold()
    {
        var original = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));

        var observed = await ReplayVotingOracle.EvaluateAsync(
            original,
            "target",
            new ReplayPolicy(5, 3),
            static (_, attempt, _) => Task.FromResult<string?>(attempt <= 2 ? "target" : null),
            CancellationToken.None);

        Assert.Null(observed);
    }
}
