using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed class UniformRandomFuzzEngine(IReadOnlyList<ICaseMutationOperator> operators)
{
    public Task<FuzzCampaignResult> RunAsync(
        CanonicalCase seed,
        int budget,
        long campaignSeed,
        Func<CanonicalCase, CancellationToken, Task<FuzzObservation>> evaluator,
        CancellationToken cancellationToken)
    => new PolicyDrivenFuzzEngine(operators, FuzzPolicySet.KeepAllUniform()).RunAsync(
        seed, budget, campaignSeed, evaluator, cancellationToken);
}
