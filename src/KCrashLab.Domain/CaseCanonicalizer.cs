using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;

namespace KCrashLab.Domain;

public static class CaseCanonicalizer
{
    public const int MaximumCaseBytes = 1_048_576;
    public const int MaximumDepth = 16;
    public const int MaximumStringLength = 65_536;
    public const int MaximumOperations = 64;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> AllowedTargets =
    [
        "kcl.bounds", "kcl.integer", "kcl.race", "kcl.state", "kcl.lifetime", "kcl.kmdf"
    ];

    private static readonly HashSet<string> AllowedOperations =
    [
        "ECHO", "RESET_STATE", "SET_MODE", "SUBMIT_RECORD", "QUERY_STATS", "TRIGGER_ASYNC"
    ];

    public static CanonicalCase Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var utf8Length = System.Text.Encoding.UTF8.GetByteCount(json);
        if (utf8Length > MaximumCaseBytes)
        {
            throw new InvalidDataException($"Case exceeds {MaximumCaseBytes} UTF-8 bytes.");
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth
        });

        ValidateTree(document.RootElement, 0);
        var value = document.RootElement.Deserialize<TestCase>(ContractJson.Compact)
            ?? throw new InvalidDataException("Case JSON did not produce a document.");
        ValidateCase(value);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }

        var canonical = stream.ToArray();
        using var identityStream = new MemoryStream();
        using (var identityWriter = new Utf8JsonWriter(identityStream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(identityWriter, document.RootElement, omitTopLevelLineage: true);
        }

        var id = Convert.ToHexString(SHA256.HashData(identityStream.ToArray())).ToLowerInvariant();
        return new CanonicalCase(value, id, canonical);
    }

    public static CanonicalCase Parse(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return Parse(StrictUtf8.GetString(utf8Json));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Case JSON is not valid UTF-8.", exception);
        }
    }

    private static void ValidateCase(TestCase value)
    {
        if (value.SchemaVersion != 1)
        {
            throw new InvalidDataException("Only case schema_version 1 is supported.");
        }

        if (!AllowedTargets.Contains(value.Target))
        {
            throw new InvalidDataException($"Target '{value.Target}' is not allowlisted.");
        }

        if (value.Seed < 0)
        {
            throw new InvalidDataException("Seed must be non-negative.");
        }

        if (value.Operations.Count > MaximumOperations)
        {
            throw new InvalidDataException($"Case contains more than {MaximumOperations} operations.");
        }

        foreach (var operation in value.Operations)
        {
            if (!AllowedOperations.Contains(operation.Ioctl))
            {
                throw new InvalidDataException($"Operation '{operation.Ioctl}' is not supported.");
            }

            if (operation.Input is { } input && UnicodeLength(input) > MaximumStringLength)
            {
                throw new InvalidDataException("Operation input is too large.");
            }
            if (operation.Fields is { Count: > 32 })
            {
                throw new InvalidDataException("Operation fields contains more than 32 properties.");
            }
        }

        if (value.ParentCaseId is not null
            && (value.ParentCaseId.Length != 64 || value.ParentCaseId.Any(static character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))))
        {
            throw new InvalidDataException("parent_case_id must be a lowercase SHA-256 digest.");
        }

        if (value.Mutation is { } mutation)
        {
            if (UnicodeLength(mutation.OperatorId) > 64)
            {
                throw new InvalidDataException("mutation.operator_id exceeds 64 characters.");
            }
            if (mutation.Parameters.Count > 32)
            {
                throw new InvalidDataException("mutation.parameters contains more than 32 properties.");
            }
        }

        if (value.Schedule is { } schedule)
        {
            if (schedule.Workers is < 1 or > 16)
            {
                throw new InvalidDataException("Schedule workers must be between 1 and 16.");
            }

            if (schedule.DelaysUs.Count > MaximumOperations || schedule.DelaysUs.Any(static delay => delay is < 0 or > 10_000_000))
            {
                throw new InvalidDataException("Schedule delay is outside the allowed range.");
            }

            if (schedule.DelaysUs.Count != value.Operations.Count)
            {
                throw new InvalidDataException("Schedule delays_us must contain one entry per operation.");
            }
        }
    }

    private static void ValidateTree(JsonElement element, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw new InvalidDataException("JSON nesting is too deep.");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!names.Add(property.Name))
                        {
                            throw new InvalidDataException($"Duplicate JSON property '{property.Name}'.");
                        }

                        if (UnicodeLength(property.Name) > 128)
                        {
                            throw new InvalidDataException("JSON property name is too long.");
                        }

                        ValidateTree(property.Value, depth + 1);
                    }

                    break;
                }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateTree(item, depth + 1);
                }

                break;
            case JsonValueKind.String:
                if (element.GetString() is { } value && UnicodeLength(value) > MaximumStringLength)
                {
                    throw new InvalidDataException("JSON string is too long.");
                }

                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out _) && !element.TryGetUInt64(out _))
                {
                    throw new InvalidDataException("Case IR v1 accepts integer JSON numbers only.");
                }

                break;
        }
    }

    private static int UnicodeLength(string value) => value.EnumerateRunes().Count();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool omitTopLevelLineage = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    if (omitTopLevelLineage && property.Name is "parent_case_id" or "mutation")
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number when element.TryGetInt64(out var signed):
                writer.WriteNumberValue(signed);
                break;
            case JsonValueKind.Number when element.TryGetUInt64(out var unsigned):
                writer.WriteNumberValue(unsigned);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"Unsupported JSON token {element.ValueKind}.");
        }
    }
}
