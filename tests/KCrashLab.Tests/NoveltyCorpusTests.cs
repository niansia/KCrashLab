using KCrashLab.Contracts;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class NoveltyCorpusTests
{
    [Fact]
    public async Task OnlyNovelOrFailingCasesEnterCorpus()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var mutations = new BoundaryScalarMutationOperator().Mutate(seed, new MutationContext(1, 2)).ToArray();
        var corpus = new NoveltyCorpus();

        var seedResult = corpus.Observe(seed, new FuzzObservation("COMPLETE", ["a", "b"], null), 1, forceAdd: true);
        var boring = corpus.Observe(mutations[0], new FuzzObservation("COMPLETE", ["a", "b"], null), 2);
        var failing = corpus.Observe(mutations[1], new FuzzObservation("SYNTHETIC_FAILURE", ["a", "b"], new string('c', 64)), 3);

        Assert.True(seedResult.Added);
        Assert.False(boring.Added);
        Assert.True(failing.Added);
        Assert.Equal(2, corpus.Count);
    }
}

