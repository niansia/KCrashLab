using System.Text.Json;

namespace KCrashLab.Tests;

public sealed class SchemaSyntaxTests
{
    [Fact]
    public void EverySchemaIsValidJsonAndDeclaresDraft202012()
    {
        var schemaRoot = Path.Combine(TestPaths.RepositoryRoot, "schemas");
        var schemas = Directory.EnumerateFiles(schemaRoot, "*.schema.json").Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(schemas);

        foreach (var path in schemas)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", document.RootElement.GetProperty("$schema").GetString());
        }
    }
}

