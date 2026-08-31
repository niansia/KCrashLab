using System.Security.Cryptography;
using System.Text;

namespace KCrashLab.Domain;

public static class DeterministicIdentity
{
    public static Guid CreateGuid(string scope, params object[] parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var material = scope + "\0" + string.Join("\0", parts.Select(static part => part.ToString() ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

