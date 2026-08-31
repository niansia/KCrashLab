using System.Diagnostics;
using KCrashLab.Contracts;
using KCrashLab.Controller;

namespace KCrashLab.Tests;

public sealed class ExperimentProvenanceBuilderTests
{
    [Fact]
    public async Task SourceCommitTimeResolvesCleanHeadAndRejectsDirtyOrMismatchedTrees()
    {
        var repository = TestPaths.NewTemporaryDirectory();
        try
        {
            await RunGitAsync(repository, "init");
            await RunGitAsync(repository, "config", "user.name", "KCrashLab Tests");
            await RunGitAsync(repository, "config", "user.email", "kcrashlab-tests@example.invalid");
            await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "first\n");
            await RunGitAsync(repository, "add", "README.md");
            await RunGitAsync(repository, "commit", "-m", "first");
            var firstCommit = await RunGitAsync(repository, "rev-parse", "HEAD");

            var resolved = await ExperimentProvenanceBuilder.ResolveGitCommitAsync(
                repository,
                ExperimentProvenanceBuilder.SourceCommitTime,
                requestedGitCommit: null,
                CancellationToken.None);
            Assert.Equal(firstCommit, resolved);

            await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "dirty\n");
            await Assert.ThrowsAsync<InvalidDataException>(() => ExperimentProvenanceBuilder.ResolveGitCommitAsync(
                repository,
                ExperimentProvenanceBuilder.SourceCommitTime,
                requestedGitCommit: null,
                CancellationToken.None));

            await RunGitAsync(repository, "add", "README.md");
            await RunGitAsync(repository, "commit", "-m", "second");
            await Assert.ThrowsAsync<InvalidDataException>(() => ExperimentProvenanceBuilder.ValidateCanonicalGitStateAsync(
                repository,
                firstCommit,
                CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryGitRepository(repository);
        }
    }

    [Fact]
    public void SourceCommitTimeNeverAcceptsUncommittedProvenance()
    {
        var digest = new string('a', 64);
        var provenance = new ExperimentProvenance(
            ExperimentProvenanceBuilder.Unspecified,
            ExperimentProvenanceBuilder.Unspecified,
            ExperimentProvenanceBuilder.SourceCommitTime,
            ExperimentProvenanceBuilder.Uncommitted,
            digest,
            digest,
            1,
            ExperimentProvenanceBuilder.EngineVersion);

        Assert.Throws<InvalidDataException>(() => ExperimentProvenanceBuilder.Validate(provenance));
    }

    private static async Task<string> RunGitAsync(string repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git test process.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static void DeleteTemporaryGitRepository(string repository)
    {
        foreach (var path in Directory.EnumerateFiles(repository, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(repository, recursive: true);
    }
}
