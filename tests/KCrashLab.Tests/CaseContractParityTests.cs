using System.Text.Json;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class CaseContractParityTests
{
    [Fact]
    public async Task SchemaAndRuntimeShareSecurityRelevantLimits()
    {
        using var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(TestPaths.RepositoryRoot, "schemas", "case.schema.json")));
        var properties = schema.RootElement.GetProperty("properties");
        Assert.Equal(32, properties.GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("fields").GetProperty("maxProperties").GetInt32());
        Assert.Equal(64, properties.GetProperty("mutation").GetProperty("properties").GetProperty("operator_id").GetProperty("maxLength").GetInt32());
        Assert.Equal(32, properties.GetProperty("mutation").GetProperty("properties").GetProperty("parameters").GetProperty("maxProperties").GetInt32());
        Assert.Contains("ECHO", properties.GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("ioctl").GetProperty("enum").EnumerateArray().Select(static item => item.GetString()));

        Assert.Throws<InvalidDataException>(() => CaseCanonicalizer.Parse(CaseWith("parent_case_id", new string('A', 64))));
        Assert.Throws<InvalidDataException>(() => CaseCanonicalizer.Parse(CaseWith("mutation", new { operator_id = new string('x', 65), parameters = new { } })));
        Assert.Throws<InvalidDataException>(() => CaseCanonicalizer.Parse(CaseWith("mutation", new { operator_id = "x", parameters = Properties(33) })));
        Assert.Throws<InvalidDataException>(() => CaseCanonicalizer.Parse(JsonSerializer.Serialize(new
        {
            schema_version = 1, target = "kcl.state", seed = 1,
            operations = new[] { new { ioctl = "SUBMIT_RECORD", fields = Properties(33) } }
        })));
    }

    [Fact]
    public async Task EveryCheckedInCaseAcceptedByRuntimeUsesAnAllowlistedSchemaTargetAndOperation()
    {
        using var schema = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(TestPaths.RepositoryRoot, "schemas", "case.schema.json")));
        var targetPattern = schema.RootElement.GetProperty("properties").GetProperty("target").GetProperty("pattern").GetString()!;
        var operations = schema.RootElement.GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("properties").GetProperty("ioctl").GetProperty("enum")
            .EnumerateArray().Select(static item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(TestPaths.RepositoryRoot, "samples", "cases"), "*.json"))
        {
            var parsed = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(path));
            Assert.Matches(targetPattern, parsed.Value.Target);
            Assert.All(parsed.Value.Operations, operation => Assert.Contains(operation.Ioctl, operations));
        }
    }

    private static string CaseWith(string name, object value)
    {
        var document = new Dictionary<string, object>
        {
            ["schema_version"] = 1, ["target"] = "kcl.state", ["seed"] = 1,
            ["operations"] = new[] { new { ioctl = "RESET_STATE" } }, [name] = value
        };
        return JsonSerializer.Serialize(document);
    }

    private static Dictionary<string, int> Properties(int count) =>
        Enumerable.Range(0, count).ToDictionary(static index => $"p{index}", static index => index, StringComparer.Ordinal);
}
