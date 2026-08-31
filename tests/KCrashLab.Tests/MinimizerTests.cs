using KCrashLab.Domain;
using KCrashLab.Simulation;

namespace KCrashLab.Tests;

public sealed class MinimizerTests
{
    [Fact]
    public async Task SyntheticTriggerMinimizesFromFourteenToThreeOperations()
    {
        var original = CaseCanonicalizer.Parse(await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-original.case.json")));
        var signature = SyntheticStateTarget.Evaluate(original);
        Assert.Equal(SyntheticStateTarget.KnownSignature, signature);

        var result = await SequenceMinimizer.MinimizeAsync(
            original,
            signature!,
            static (candidate, _) => Task.FromResult(SyntheticStateTarget.Evaluate(candidate)),
            128,
            CancellationToken.None);

        Assert.Equal(3, result.Minimized.Value.Operations.Count);
        Assert.True(result.OperationReduction >= 0.70);
        Assert.Equal(signature, SyntheticStateTarget.Evaluate(result.Minimized));
    }
}

