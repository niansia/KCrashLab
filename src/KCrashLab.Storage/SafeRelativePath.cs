namespace KCrashLab.Storage;

public static class SafeRelativePath
{
    public static string Normalize(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Artifact path must be relative.");
        }

        var segments = normalized.Split('/');
        if (segments.Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException("Artifact path contains an unsafe segment.");
        }

        return string.Join('/', segments);
    }

    public static string ResolveExistingFile(string root, string relativePath)
    {
        var normalized = Normalize(relativePath);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(rootFull, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!target.StartsWith(rootFull, comparison))
        {
            throw new InvalidDataException("Artifact path escapes the evidence root.");
        }

        var cursor = rootFull.TrimEnd(Path.DirectorySeparatorChar);
        foreach (var segment in normalized.Split('/'))
        {
            cursor = Path.Combine(cursor, segment);
            if (File.Exists(cursor) || Directory.Exists(cursor))
            {
                var attributes = File.GetAttributes(cursor);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Reparse points are not allowed inside evidence bundles.");
                }
            }
        }

        if (!File.Exists(target))
        {
            throw new FileNotFoundException("Manifest entry is missing.", target);
        }

        return target;
    }
}

