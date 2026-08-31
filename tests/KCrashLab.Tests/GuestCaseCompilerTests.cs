using System.Buffers.Binary;
using KCrashLab.Domain;
using KCrashLab.GuestAgent;

namespace KCrashLab.Tests;

public sealed class GuestCaseCompilerTests
{
    [Fact]
    public void CompilesAllowlistedOperationsDeterministically()
    {
        var testCase = CaseCanonicalizer.Parse(
            """
            {"schema_version":1,"target":"kcl.kmdf","seed":7,"operations":[{"ioctl":"RESET_STATE"},{"ioctl":"SET_MODE","fields":{"mode":2}},{"ioctl":"SUBMIT_RECORD","fields":{"declared_len":64,"payload":"AAAA"}}],"schedule":{"workers":1,"delays_us":[0,0,0]}}
            """);

        var first = GuestCaseCompiler.Compile(testCase);
        var second = GuestCaseCompiler.Compile(testCase);

        Assert.Equal(new[] { GuestCaseCompiler.ResetState, GuestCaseCompiler.SetMode, GuestCaseCompiler.SubmitRecord },
            first.Select(static request => request.ControlCode));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(first[1].Input));
        Assert.Equal(64u, BinaryPrimitives.ReadUInt32LittleEndian(first[2].Input));
        Assert.Equal(first.Select(static request => request.Input), second.Select(static request => request.Input), ByteArrayComparer.Instance);
    }

    [Theory]
    [InlineData("kcl.state", "ECHO")]
    [InlineData("kcl.kmdf", "QUERY_STATS")]
    public void RejectsTargetsAndOperationsOutsideTheAllowlist(string target, string operation)
    {
        var testCase = CaseCanonicalizer.Parse($$"""
            {"schema_version":1,"target":"{{target}}","seed":7,"operations":[{"ioctl":"{{operation}}"}]}
            """);

        Assert.Throws<InvalidDataException>(() => GuestCaseCompiler.Compile(testCase));
    }

    [Fact]
    public void RejectsUnsupportedSchedulingInsteadOfIgnoringIt()
    {
        var testCase = CaseCanonicalizer.Parse(
            """{"schema_version":1,"target":"kcl.kmdf","seed":7,"operations":[{"ioctl":"RESET_STATE"}],"schedule":{"workers":1,"delays_us":[1]}}""");
        Assert.Throws<InvalidDataException>(() => GuestCaseCompiler.Compile(testCase));
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public bool Equals(byte[]? left, byte[]? right) => left is not null && right is not null && left.SequenceEqual(right);
        public int GetHashCode(byte[] value) => value.Aggregate(17, static (hash, item) => HashCode.Combine(hash, item));
    }
}
