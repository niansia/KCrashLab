namespace KCrashLab.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "KCrashLab.sln")))
            {
                current = current.Parent;
            }

            return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }

    public static string Sample(params string[] parts) =>
        Path.Combine([RepositoryRoot, "samples", .. parts]);

    public static string Fixture(params string[] parts) =>
        Path.Combine([RepositoryRoot, "tests", "fixtures", .. parts]);

    public static string NewTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "KCrashLab.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void AssertLfUtf8NoBom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.DoesNotContain((byte)'\r', bytes);
    }
}
