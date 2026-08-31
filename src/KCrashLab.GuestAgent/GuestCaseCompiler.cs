using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using KCrashLab.Contracts;

namespace KCrashLab.GuestAgent;

public sealed record GuestIoctlRequest(string Operation, uint ControlCode, byte[] Input);

public static class GuestCaseCompiler
{
    public const string Target = "kcl.kmdf";
    public const uint Echo = 0x0022E004;
    public const uint ResetState = 0x0022E008;
    public const uint SetMode = 0x0022E00C;
    public const uint SubmitRecord = 0x0022E010;

    public static IReadOnlyList<GuestIoctlRequest> Compile(CanonicalCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        if (!string.Equals(testCase.Value.Target, Target, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Guest agent only permits target '{Target}'.");
        }

        if (testCase.Value.Schedule is { Workers: not 1 })
        {
            throw new InvalidDataException("Track B v0.2 requires a single worker.");
        }
        if (testCase.Value.Schedule?.DelaysUs.Any(static delay => delay != 0) == true)
        {
            throw new InvalidDataException("Track B v0.2 does not silently ignore non-zero schedule delays.");
        }

        return testCase.Value.Operations.Select(CompileOperation).ToArray();
    }

    private static GuestIoctlRequest CompileOperation(CaseOperation operation) => operation.Ioctl switch
    {
        "ECHO" => new GuestIoctlRequest(operation.Ioctl, Echo, DecodeInput(operation.Input)),
        "RESET_STATE" => new GuestIoctlRequest(operation.Ioctl, ResetState, []),
        "SET_MODE" => new GuestIoctlRequest(operation.Ioctl, SetMode, UInt32Field(operation, "mode")),
        "SUBMIT_RECORD" => new GuestIoctlRequest(operation.Ioctl, SubmitRecord, RecordInput(operation)),
        _ => throw new InvalidDataException($"IOCTL operation '{operation.Ioctl}' is not allowlisted for Track B.")
    };

    private static byte[] RecordInput(CaseOperation operation)
    {
        var declaredLength = RequiredUInt32(operation, "declared_len");
        var payload = RequiredString(operation, "payload");
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        if (payloadBytes.Length > 4_096)
        {
            throw new InvalidDataException("SUBMIT_RECORD payload exceeds 4096 bytes.");
        }

        var result = new byte[sizeof(uint) + payloadBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, declaredLength);
        payloadBytes.CopyTo(result.AsSpan(sizeof(uint)));
        return result;
    }

    private static byte[] UInt32Field(CaseOperation operation, string name)
    {
        var result = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(result, RequiredUInt32(operation, name));
        return result;
    }

    private static uint RequiredUInt32(CaseOperation operation, string name)
    {
        var value = RequiredField(operation, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var parsed)
            ? parsed
            : throw new InvalidDataException($"Field '{name}' must be an unsigned 32-bit integer.");
    }

    private static string RequiredString(CaseOperation operation, string name)
    {
        var value = RequiredField(operation, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"Field '{name}' must be a string.");
    }

    private static JsonElement RequiredField(CaseOperation operation, string name) =>
        operation.Fields is not null && operation.Fields.TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException($"Operation '{operation.Ioctl}' requires field '{name}'.");

    private static byte[] DecodeInput(string? input)
    {
        if (input is null)
        {
            return [];
        }

        try
        {
            return Convert.FromBase64String(input);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("ECHO input must be canonical base64.", exception);
        }
    }
}
