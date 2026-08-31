using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using KCrashLab.Domain;

namespace KCrashLab.Tests;

public sealed class CaseContractParityTests
{
    private static readonly Lazy<JsonSchema> CaseSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, "schemas", "case.schema.json"))));

    private static readonly EvaluationOptions SchemaEvaluationOptions = new()
    {
        OutputFormat = OutputFormat.Flag
    };

    [Fact]
    public void RuntimeAndDraft202012SchemaAgreeOnGeneratedBoundaryCorpus()
    {
        var schema = LoadSchema();
        foreach (var testCase in BoundaryCorpus())
        {
            var schemaAccepted = SchemaAccepts(schema, testCase.Json);
            var runtimeAccepted = RuntimeAccepts(testCase.Json);
            Assert.True(
                schemaAccepted == testCase.Expected && runtimeAccepted == testCase.Expected,
                $"{testCase.Name}: expected {testCase.Expected}, schema={schemaAccepted}, runtime={runtimeAccepted}");
        }
    }

    [Fact]
    public void RandomizedPropertyOrderIsAcceptedAndCanonicalizesToOneIdentity()
    {
        var schema = LoadSchema();
        var baseline = ValidCase();
        var expectedCaseId = CaseCanonicalizer.Parse(baseline.ToJsonString()).CaseId;
        var properties = baseline.ToArray();
        var random = new Random(20260831);

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var shuffled = properties.OrderBy(_ => random.Next()).ToArray();
            var reordered = new JsonObject();
            foreach (var property in shuffled)
            {
                reordered.Add(property.Key, property.Value?.DeepClone());
            }

            var json = reordered.ToJsonString();
            Assert.True(SchemaAccepts(schema, json));
            Assert.Equal(expectedCaseId, CaseCanonicalizer.Parse(json).CaseId);
        }
    }

    [Fact]
    public async Task EveryCheckedInCaseIsAcceptedByBothValidators()
    {
        var schema = LoadSchema();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(TestPaths.RepositoryRoot, "samples", "cases"), "*.json"))
        {
            var json = await File.ReadAllTextAsync(path);
            Assert.True(SchemaAccepts(schema, json), $"Schema rejected {Path.GetFileName(path)}.");
            Assert.True(RuntimeAccepts(json), $"Runtime rejected {Path.GetFileName(path)}.");
        }
    }

    [Fact]
    public void RuntimeEnforcesTransportSizeBeforeSemanticContractEvaluation()
    {
        var oversized = ValidCase().ToJsonString() + new string(' ', CaseCanonicalizer.MaximumCaseBytes);
        Assert.False(RuntimeAccepts(oversized));
        Assert.True(SchemaAccepts(LoadSchema(), oversized));
    }

    private static IEnumerable<ParityCase> BoundaryCorpus()
    {
        yield return Case("baseline", ValidCase(), expected: true);
        yield return Case("maximum seed", With(ValidCase(), "seed", long.MaxValue), expected: true);
        yield return Case("negative seed", With(ValidCase(), "seed", -1), expected: false);
        yield return Case("seed above Int64", With(ValidCase(), "seed", JsonNode.Parse("9223372036854775808")), expected: false);
        yield return Case("maximum input length", WithOperationInput(65_536), expected: true);
        yield return Case("input above maximum", WithOperationInput(65_537), expected: false);
        yield return Case("maximum operations", WithOperations(64), expected: true);
        yield return Case("operations above maximum", WithOperations(65), expected: false);
        yield return Case("maximum fields", WithFields(32), expected: true);
        yield return Case("fields above maximum", WithFields(33), expected: false);
        yield return Case("maximum mutation operator length", WithMutation(new string('x', 64), new JsonObject()), expected: true);
        yield return Case("mutation operator above maximum", WithMutation(new string('x', 65), new JsonObject()), expected: false);
        yield return Case("maximum mutation parameters", WithMutation("x", Properties(32)), expected: true);
        yield return Case("mutation parameters above maximum", WithMutation("x", Properties(33)), expected: false);
        yield return Case("lowercase parent digest", With(ValidCase(), "parent_case_id", new string('a', 64)), expected: true);
        yield return Case("uppercase parent digest", With(ValidCase(), "parent_case_id", new string('A', 64)), expected: false);
        yield return Case("minimum signed field integer", WithFieldValue(JsonNode.Parse("-9223372036854775808")), expected: true);
        yield return Case("below signed and unsigned integer domain", WithFieldValue(JsonNode.Parse("-9223372036854775809")), expected: false);
        yield return Case("maximum unsigned field integer", WithFieldValue(JsonNode.Parse("18446744073709551615")), expected: true);
        yield return Case("above signed and unsigned integer domain", WithFieldValue(JsonNode.Parse("18446744073709551616")), expected: false);
        yield return Case("floating point field", WithFieldValue(JsonNode.Parse("1.5")), expected: false);
        yield return Case("maximum field property name", WithNamedField(new string('p', 128)), expected: true);
        yield return Case("field property name above maximum", WithNamedField(new string('p', 129)), expected: false);
        yield return Case("maximum nested parameter depth", WithNestedParameter(13), expected: true);
        yield return Case("parameter depth above maximum", WithNestedParameter(14), expected: false);
        yield return Case("schedule lower boundaries", WithSchedule(1, 0), expected: true);
        yield return Case("schedule upper boundaries", WithSchedule(16, 10_000_000), expected: true);
        yield return Case("schedule worker below minimum", WithSchedule(0, 0), expected: false);
        yield return Case("schedule worker above maximum", WithSchedule(17, 0), expected: false);
        yield return Case("schedule delay below minimum", WithSchedule(1, -1), expected: false);
        yield return Case("schedule delay above maximum", WithSchedule(1, 10_000_001), expected: false);
        yield return Case("missing required target", Without(ValidCase(), "target"), expected: false);
        yield return Case("unknown top-level property", With(ValidCase(), "unexpected", 1), expected: false);
        yield return Case("unknown operation property", WithUnknownOperationProperty(), expected: false);
    }

    private static JsonSchema LoadSchema() => CaseSchema.Value;

    private static bool SchemaAccepts(JsonSchema schema, string json)
    {
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(document.RootElement, SchemaEvaluationOptions).IsValid;
    }

    private static bool RuntimeAccepts(string json) => Record.Exception(() => CaseCanonicalizer.Parse(json)) is null;

    private static ParityCase Case(string name, JsonObject document, bool expected) =>
        new(name, document.ToJsonString(), expected);

    private static JsonObject ValidCase() => new()
    {
        ["schema_version"] = 1,
        ["target"] = "kcl.state",
        ["seed"] = 1,
        ["operations"] = Operations(1)
    };

    private static JsonObject With(JsonObject root, string name, JsonNode? value)
    {
        root[name] = value;
        return root;
    }

    private static JsonObject Without(JsonObject root, string name)
    {
        root.Remove(name);
        return root;
    }

    private static JsonObject WithOperationInput(int length)
    {
        var root = ValidCase();
        root["operations"]![0]!["input"] = new string('a', length);
        return root;
    }

    private static JsonObject WithOperations(int count) => With(ValidCase(), "operations", Operations(count));

    private static JsonObject WithFields(int count) => With(ValidCase(), "operations", new JsonArray
    {
        new JsonObject { ["ioctl"] = "SUBMIT_RECORD", ["fields"] = Properties(count) }
    });

    private static JsonObject WithMutation(string operatorId, JsonObject parameters) => With(
        ValidCase(),
        "mutation",
        new JsonObject { ["operator_id"] = operatorId, ["parameters"] = parameters });

    private static JsonObject WithFieldValue(JsonNode? value) => With(ValidCase(), "operations", new JsonArray
    {
        new JsonObject
        {
            ["ioctl"] = "SET_MODE",
            ["fields"] = new JsonObject { ["value"] = value }
        }
    });

    private static JsonObject WithNamedField(string name) => With(ValidCase(), "operations", new JsonArray
    {
        new JsonObject
        {
            ["ioctl"] = "SET_MODE",
            ["fields"] = new JsonObject { [name] = 1 }
        }
    });

    private static JsonObject WithNestedParameter(int objectCount)
    {
        JsonNode value = JsonValue.Create(1)!;
        for (var index = 0; index < objectCount; index++)
        {
            value = new JsonObject { ["child"] = value };
        }

        return WithMutation("nested", new JsonObject { ["value"] = value });
    }

    private static JsonObject WithSchedule(int workers, long delay) => With(
        ValidCase(),
        "schedule",
        new JsonObject { ["workers"] = workers, ["delays_us"] = new JsonArray(delay) });

    private static JsonObject WithUnknownOperationProperty()
    {
        var root = ValidCase();
        root["operations"]![0]!["unexpected"] = true;
        return root;
    }

    private static JsonArray Operations(int count)
    {
        var operations = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            operations.Add(new JsonObject { ["ioctl"] = "RESET_STATE" });
        }

        return operations;
    }

    private static JsonObject Properties(int count)
    {
        var properties = new JsonObject();
        for (var index = 0; index < count; index++)
        {
            properties.Add($"p{index}", index);
        }

        return properties;
    }

    private sealed record ParityCase(string Name, string Json, bool Expected);
}
