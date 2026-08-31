namespace KCrashLab;

internal static class StorageNameRules
{
    public static string Normalize(string relativeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeName);
        var normalized = relativeName.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Artifact name must be relative.");
        }

        var parts = normalized.Split('/');
        if (parts.Any(static part => part.Length == 0 || part is "." or ".."))
        {
            throw new InvalidDataException("Artifact name contains an unsafe segment.");
        }

        return normalized;
    }
}

