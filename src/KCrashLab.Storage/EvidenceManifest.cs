using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KCrashLab.Storage;

public sealed record ManifestEntry(string Sha256, string RelativePath, long Length);

public sealed record ManifestVerification(
    bool IsValid,
    IReadOnlyList<ManifestEntry> Verified,
    IReadOnlyList<string> Errors);

public static partial class EvidenceManifest
{
    public const string FileName = "manifest.sha256";
    public const int MaximumEntries = 10_000;

    public static async Task<IReadOnlyList<ManifestEntry>> CreateAsync(
        string bundleRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(bundleRoot);
        Directory.CreateDirectory(root);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                Relative = Path.GetRelativePath(root, path).Replace('\\', '/')
            })
            .Where(static item => !string.Equals(item.Relative, FileName, StringComparison.Ordinal))
            .OrderBy(static item => item.Relative, StringComparer.Ordinal)
            .ToArray();
        if (files.Length > MaximumEntries)
        {
            throw new InvalidDataException("Evidence bundle contains too many files.");
        }

        var entries = new List<ManifestEntry>(files.Length);
        foreach (var file in files)
        {
            var safePath = SafeRelativePath.ResolveExistingFile(root, file.Relative);
            await using var stream = new FileStream(safePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            entries.Add(new ManifestEntry(hash, file.Relative, stream.Length));
        }

        var content = string.Concat(entries.Select(static entry => $"{entry.Sha256}  {entry.RelativePath}\n"));
        var temporary = Path.Combine(root, $".{FileName}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        await using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, Path.Combine(root, FileName), overwrite: true);
        return entries;
    }

    public static async Task<ManifestVerification> VerifyAsync(
        string bundleRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(bundleRoot);
        var manifestPath = Path.Combine(root, FileName);
        var errors = new List<string>();
        var verified = new List<ManifestEntry>();
        if (!File.Exists(manifestPath))
        {
            return new ManifestVerification(false, verified, ["manifest.sha256 is missing."]);
        }

        var lines = await File.ReadAllLinesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (lines.Length > MaximumEntries)
        {
            return new ManifestVerification(false, verified, ["Manifest contains too many entries."]);
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = ManifestLineRegex().Match(line);
            if (!match.Success)
            {
                errors.Add("Malformed manifest line.");
                continue;
            }

            var expected = match.Groups[1].Value.ToLowerInvariant();
            var relative = match.Groups[2].Value;
            if (!paths.Add(relative))
            {
                errors.Add($"Duplicate manifest path: {relative}");
                continue;
            }

            try
            {
                var fullPath = SafeRelativePath.ResolveExistingFile(root, relative);
                await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    errors.Add($"Hash mismatch: {relative}");
                    continue;
                }

                verified.Add(new ManifestEntry(actual, relative, stream.Length));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Cannot verify {relative}: {exception.Message}");
            }
        }

        var actualPaths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(static path => !string.Equals(path, FileName, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var untracked in actualPaths.Except(paths).Order(StringComparer.Ordinal))
        {
            errors.Add($"Untracked evidence file: {untracked}");
        }

        return new ManifestVerification(errors.Count == 0, verified, errors);
    }

    [GeneratedRegex("^([a-fA-F0-9]{64})  ([^\\r\\n]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ManifestLineRegex();
}
