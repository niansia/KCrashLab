namespace KCrashLab.Domain;

public sealed class ExactCrashIndex
{
    private readonly Dictionary<string, List<Guid>> clusters = new(StringComparer.Ordinal);

    public void Add(string signature, Guid findingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        if (!clusters.TryGetValue(signature, out var findings))
        {
            findings = [];
            clusters.Add(signature, findings);
        }

        if (!findings.Contains(findingId))
        {
            findings.Add(findingId);
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<Guid>> Snapshot() =>
        clusters.ToDictionary(
            static item => item.Key,
            static item => (IReadOnlyList<Guid>)item.Value.Order().ToArray(),
            StringComparer.Ordinal);
}

