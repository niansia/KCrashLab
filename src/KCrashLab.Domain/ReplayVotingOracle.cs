using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static class ReplayVotingOracle
{
    public static async Task<string?> EvaluateAsync(
        CanonicalCase candidate,
        string targetSignature,
        ReplayPolicy policy,
        Func<CanonicalCase, int, CancellationToken, Task<string?>> attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSignature);
        ArgumentNullException.ThrowIfNull(attempt);

        var decision = await ReplayEngine.RunAsync(
            policy,
            (attemptNumber, innerCancellationToken) => attempt(candidate, attemptNumber, innerCancellationToken),
            targetSignature,
            cancellationToken).ConfigureAwait(false);
        return decision.Passed ? targetSignature : null;
    }
}
