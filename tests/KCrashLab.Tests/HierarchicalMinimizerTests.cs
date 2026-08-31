using KCrashLab.Domain;
using KCrashLab.Simulation;

namespace KCrashLab.Tests;

public sealed class HierarchicalMinimizerTests
{
    [Fact]
    public async Task SyntheticTriggerShrinksSequenceFieldsAndCanonicalBytes()
    {
        var original = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));
        var signature = SyntheticStateTarget.Evaluate(original);

        var result = await HierarchicalMinimizer.MinimizeAsync(
            original,
            signature!,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Evaluate(candidate)),
            maximumAttempts: 256,
            CancellationToken.None);

        Assert.Equal(3, result.Minimized.Value.Operations.Count);
        Assert.True(result.OperationReduction >= 0.70);
        Assert.True(result.ByteReduction >= 0.60);
        Assert.Null(result.Minimized.Value.Schedule);
        Assert.Equal(signature, SyntheticStateTarget.Evaluate(result.Minimized));
        Assert.Equal("LOCAL_MINIMUM_HIERARCHICAL_V1", result.StopReason);

        var submit = result.Minimized.Value.Operations.Single(static operation => operation.Ioctl == "SUBMIT_RECORD");
        Assert.NotNull(submit.Fields);
        Assert.False(submit.Fields.ContainsKey("payload"));
        Assert.Equal(1, submit.Fields["declared_len"].GetInt32());
    }
}
