using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public sealed record CorpusObservation(bool Added, int NovelCoverage);

public sealed class NoveltyCorpus
{
    private readonly Dictionary<string, MutableCorpusEntry> entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> globalCoverage = new(StringComparer.Ordinal);

    public IReadOnlySet<string> GlobalCoverage => globalCoverage;

    public int Count => entries.Count;

    public CorpusObservation Observe(
        CanonicalCase testCase,
        FuzzObservation observation,
        int execution,
        bool forceAdd = false)
    {
        var coverage = observation.Coverage.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var novelty = coverage.Count(item => !globalCoverage.Contains(item));
        globalCoverage.UnionWith(coverage);
        var interesting = forceAdd || novelty > 0 || observation.Signature is not null;
        if (!interesting || entries.ContainsKey(testCase.CaseId))
        {
            return new CorpusObservation(false, novelty);
        }

        var energy = Math.Max(1, 1 + (novelty * 2) + (observation.Signature is null ? 0 : 8));
        entries.Add(testCase.CaseId, new MutableCorpusEntry(testCase, execution, novelty, energy, coverage));
        return new CorpusObservation(true, novelty);
    }

    public CanonicalCase SelectNext(int iteration)
    {
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Cannot select from an empty corpus.");
        }

        var selected = entries.Values
            .OrderByDescending(static entry => (double)entry.Energy / (entry.Selections + 1))
            .ThenBy(static entry => entry.LastSelectedIteration)
            .ThenBy(static entry => entry.TestCase.CaseId, StringComparer.Ordinal)
            .First();
        selected.Selections++;
        selected.LastSelectedIteration = iteration;
        return selected.TestCase;
    }

    public IReadOnlyList<CorpusEntrySnapshot> Snapshot() =>
        entries.Values
            .OrderBy(static entry => entry.DiscoveryExecution)
            .ThenBy(static entry => entry.TestCase.CaseId, StringComparer.Ordinal)
            .Select(static entry => new CorpusEntrySnapshot(
                entry.TestCase,
                entry.DiscoveryExecution,
                entry.NovelCoverage,
                entry.Energy,
                entry.Selections,
                entry.Coverage))
            .ToArray();

    private sealed class MutableCorpusEntry(
        CanonicalCase testCase,
        int discoveryExecution,
        int novelCoverage,
        int energy,
        IReadOnlyList<string> coverage)
    {
        public CanonicalCase TestCase { get; } = testCase;

        public int DiscoveryExecution { get; } = discoveryExecution;

        public int NovelCoverage { get; } = novelCoverage;

        public int Energy { get; } = energy;

        public IReadOnlyList<string> Coverage { get; } = coverage;

        public int Selections { get; set; }

        public int LastSelectedIteration { get; set; } = -1;
    }
}
