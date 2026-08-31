using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static class ReplayEngine
{
    public static async Task<ReplayDecision> RunAsync(
        ReplayPolicy policy,
        Func<int, CancellationToken, Task<string?>> attempt,
        string targetSignature,
        CancellationToken cancellationToken)
    {
        if (policy.Attempts < 1 || policy.RequiredMatches < 1 || policy.RequiredMatches > policy.Attempts)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        var attempts = new List<ReplayAttempt>(policy.Attempts);
        for (var index = 1; index <= policy.Attempts; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var observed = await attempt(index, cancellationToken).ConfigureAwait(false);
                var classification = observed is null
                    ? ReplayAttemptClass.NoFailure
                    : string.Equals(observed, targetSignature, StringComparison.Ordinal)
                        ? ReplayAttemptClass.Match
                        : ReplayAttemptClass.Divergent;
                attempts.Add(new ReplayAttempt(index, classification, observed));
            }
            catch (IOException)
            {
                attempts.Add(new ReplayAttempt(index, ReplayAttemptClass.InfrastructureError, null));
            }
        }

        var eligible = attempts.Count(static item => item.Classification != ReplayAttemptClass.InfrastructureError);
        var matches = attempts.Count(static item => item.Classification == ReplayAttemptClass.Match);
        return new ReplayDecision(policy, attempts, eligible, matches, eligible >= policy.RequiredMatches && matches >= policy.RequiredMatches);
    }
}

