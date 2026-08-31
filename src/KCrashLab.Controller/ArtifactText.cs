using System.Text;
using System.Text.Json;

namespace KCrashLab.Controller;

internal static class ArtifactText
{
    private static readonly UTF8Encoding Utf8NoBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Utf8NoBom.GetBytes(NormalizeNewlines(value));
    }

    public static byte[] SerializeJson(object value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        return Encode(JsonSerializer.Serialize(value, options));
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
