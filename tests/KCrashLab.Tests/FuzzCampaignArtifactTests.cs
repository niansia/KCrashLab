using System.Text.Json;
using System.Text.Json.Nodes;
using KCrashLab.Contracts;
using KCrashLab.Controller;
using KCrashLab.Domain;
using KCrashLab.Simulation;
using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class FuzzCampaignArtifactTests
{
    [Fact]
    public async Task CampaignArtifactsAreDeterministicAndTamperEvident()
    {
        var temporary = TestPaths.NewTemporaryDirectory();
        try
        {
            var seed = CaseCanonicalizer.Parse(
                await File.ReadAllTextAsync(TestPaths.Sample("cases", "state-safe-seed.case.json")));
            var result = await new DeterministicFuzzEngine(DefaultMutationOperators.Create()).RunAsync(
                seed,
                budget: 256,
                campaignSeed: 20260831,
                static (candidate, _) => Task.FromResult(SyntheticStateTarget.Observe(candidate)),
                CancellationToken.None);
            var first = Path.Combine(temporary, "first");
            var second = Path.Combine(temporary, "second");

            await FuzzCampaignArtifacts.BuildAsync(first, result, CancellationToken.None);
            await FuzzCampaignArtifacts.BuildAsync(second, result, CancellationToken.None);

            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(first, EvidenceManifest.FileName)),
                await File.ReadAllTextAsync(Path.Combine(second, EvidenceManifest.FileName)));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "metrics.csv"));
            TestPaths.AssertLfUtf8NoBom(Path.Combine(first, "report", "index.html"));
            var valid = await FuzzCampaignArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Errors));

            var untrackedPath = Path.Combine(first, "untracked.txt");
            await File.WriteAllTextAsync(untrackedPath, "not in manifest");
            var untracked = await FuzzCampaignArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.False(untracked.IsValid);
            Assert.Contains(untracked.Errors, static error => error.Contains("Untracked evidence file", StringComparison.Ordinal));
            File.Delete(untrackedPath);

            var summaryPath = Path.Combine(first, "summary.json");
            var summary = JsonNode.Parse(await File.ReadAllBytesAsync(summaryPath))!.AsObject();
            summary["max_candidates"] = 999;
            await File.WriteAllBytesAsync(summaryPath, JsonSerializer.SerializeToUtf8Bytes(summary, ContractJson.Indented));
            _ = await EvidenceManifest.CreateAsync(first, CancellationToken.None);
            var semanticallyInvalid = await FuzzCampaignArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.False(semanticallyInvalid.IsValid);
            Assert.Contains(semanticallyInvalid.Errors, static error => error.Contains("scheduler termination telemetry", StringComparison.Ordinal));

            summary["max_candidates"] = MutationCandidateSampling.DefaultMaximumCandidatesPerOperator;
            summary["scheduling_limit"] = summary["scheduling_limit"]!.GetValue<int>() + 1;
            await File.WriteAllBytesAsync(summaryPath, JsonSerializer.SerializeToUtf8Bytes(summary, ContractJson.Indented));
            _ = await EvidenceManifest.CreateAsync(first, CancellationToken.None);
            var invalidLimit = await FuzzCampaignArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.False(invalidLimit.IsValid);
            Assert.Contains(invalidLimit.Errors, static error => error.Contains("scheduler termination telemetry", StringComparison.Ordinal));

            await File.AppendAllTextAsync(Path.Combine(first, "summary.json"), " ");
            var corrupt = await FuzzCampaignArtifacts.VerifyAsync(first, CancellationToken.None);
            Assert.False(corrupt.IsValid);
            Assert.Contains(corrupt.Errors, static error => error.Contains("Hash mismatch", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void SchedulingLimitPolicyIsVersionedAndDeterministic()
    {
        Assert.Equal("MAX_4096_OR_BUDGET_X_OPERATOR_COUNT_X_32_V1", FuzzSchedulingPolicy.AlgorithmId);
        Assert.Equal(4_096, FuzzSchedulingPolicy.ComputeIterationLimit(1, 5));
        Assert.Equal(160_000, FuzzSchedulingPolicy.ComputeIterationLimit(1_000, 5));
    }
}
