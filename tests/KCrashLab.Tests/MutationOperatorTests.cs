using System.Text;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class MutationOperatorTests
{
    [Fact]
    public async Task EveryDefaultMutationPreservesSchemaAndRecordsLineage()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var context = new MutationContext(1234, MaximumCandidatesPerOperator: 16);

        foreach (var mutationOperator in DefaultMutationOperators.Create())
        {
            var first = mutationOperator.Mutate(seed, context).ToArray();
            var second = mutationOperator.Mutate(seed, context).ToArray();

            Assert.NotEmpty(first);
            Assert.Equal(first.Select(static item => item.CaseId), second.Select(static item => item.CaseId));
            foreach (var candidate in first)
            {
                Assert.Equal(seed.CaseId, candidate.Value.ParentCaseId);
                Assert.Equal(mutationOperator.OperatorId, candidate.Value.Mutation?.OperatorId);
                Assert.Equal(candidate.Value.Operations.Count, candidate.Value.Schedule?.DelaysUs.Count);
                Assert.Equal(seed.Value.Target, candidate.Value.Target);
                Assert.Equal(seed.Value.Seed, candidate.Value.Seed);
            }
        }
    }

    [Fact]
    public async Task LineageDoesNotChangeSemanticCaseId()
    {
        var seed = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
        var json = Encoding.UTF8.GetString(seed.CanonicalUtf8).TrimEnd('}');
        var withLineage = CaseCanonicalizer.Parse(
            json + $",\"parent_case_id\":\"{new string('a', 64)}\",\"mutation\":{{\"operator_id\":\"test\",\"parameters\":{{}}}}}}");

        Assert.Equal(seed.CaseId, withLineage.CaseId);
        Assert.NotEqual(seed.CanonicalUtf8, withLineage.CanonicalUtf8);
    }
}

