using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed class SequenceLengthCappedMutationOperator(
    ICaseMutationOperator inner,
    int maximumSequenceLength) : ICaseMutationOperator
{
    public string OperatorId => inner.OperatorId;

    public IEnumerable<CanonicalCase> Mutate(CanonicalCase source, MutationContext context)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSequenceLength, 1);
        return inner.Mutate(source, context)
            .Where(candidate => candidate.Value.Operations.Count <= maximumSequenceLength);
    }
}
