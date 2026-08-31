using KCrashLab.Storage;

namespace KCrashLab.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("nested//empty.txt")]
    public void UnsafeArtifactPathsAreRejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => SafeRelativePath.Normalize(path));
    }
}

