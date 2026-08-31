using System.Text;
using System.Text.Json;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class CaseCanonicalizerTests
{
    [Fact]
    public void PropertyOrderAndWhitespaceDoNotChangeCaseId()
    {
        const string first = """
            {"schema_version":1,"target":"kcl.state","seed":7,"operations":[{"ioctl":"QUERY_STATS"}]}
            """;
        const string second = """
            {
              "operations": [{ "ioctl": "QUERY_STATS" }],
              "seed": 7,
              "target": "kcl.state",
              "schema_version": 1
            }
            """;

        var a = CaseCanonicalizer.Parse(first);
        var b = CaseCanonicalizer.Parse(second);

        Assert.Equal(a.CaseId, b.CaseId);
        Assert.Equal(Encoding.UTF8.GetString(a.CanonicalUtf8), Encoding.UTF8.GetString(b.CanonicalUtf8));
    }

    [Fact]
    public void DuplicatePropertiesAreRejected()
    {
        const string json = """
            {"schema_version":1,"schema_version":1,"target":"kcl.state","seed":7,"operations":[]}
            """;

        Assert.Throws<InvalidDataException>(() => CaseCanonicalizer.Parse(json));
    }

    [Fact]
    public void UnknownContractMembersAreRejected()
    {
        const string json = """
            {"schema_version":1,"target":"kcl.state","seed":7,"operations":[],"unsafe_extension":true}
            """;

        Assert.Throws<JsonException>(() => CaseCanonicalizer.Parse(json));
    }

    [Fact]
    public void InvalidUtf8BytesAreRejectedBeforeJsonCanonicalization()
    {
        byte[] invalidUtf8 = [0x7b, 0x22, 0x78, 0x22, 0x3a, 0x22, 0xff, 0x22, 0x7d];

        var exception = Assert.Throws<InvalidDataException>(() => CaseCanonicalizer.Parse(invalidUtf8));

        Assert.IsType<DecoderFallbackException>(exception.InnerException);
    }
}
