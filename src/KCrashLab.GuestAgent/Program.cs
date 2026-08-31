using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using KCrashLab.Domain;
using Microsoft.Win32.SafeHandles;

namespace KCrashLab.GuestAgent;

internal static class Program
{
    private const string DevicePath = @"\\.\KCrashLabTarget";
    private const string DriverFileName = "KCrashLabTarget.sys";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The guest agent runs only inside the disposable Windows lab VM.");
            }

            var casePath = Required(args, "--case");
            var driverPath = Path.GetFullPath(Required(args, "--driver"));
            var expectedHash = Required(args, "--expected-driver-sha256").ToLowerInvariant();
            var journalPath = Path.GetFullPath(Required(args, "--journal"));
            ValidateDriver(driverPath, expectedHash);
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath)
                ?? throw new InvalidDataException("Journal path must have a parent directory."));
            var canonical = CaseCanonicalizer.Parse(await File.ReadAllBytesAsync(casePath).ConfigureAwait(false));
            var requests = GuestCaseCompiler.Compile(canonical);
            var attemptId = Guid.NewGuid();

            await using var journal = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
            await AppendAsync(journal, new { event_name = "ATTEMPT_PREPARED", attempt_id = attemptId, case_id = canonical.CaseId,
                driver_sha256 = expectedHash, operation_count = requests.Count }).ConfigureAwait(false);

            using var device = CreateFile(DevicePath, 0xC0000000, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (device.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the allowlisted KCrashLab device.");
            }

            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                await AppendAsync(journal, new { event_name = "OPERATION_DISPATCHING", attempt_id = attemptId,
                    case_id = canonical.CaseId, operation_index = index, operation = request.Operation,
                    control_code = request.ControlCode, input_sha256 = Convert.ToHexString(SHA256.HashData(request.Input)).ToLowerInvariant() }).ConfigureAwait(false);
                var output = request.Operation == "ECHO" ? new byte[request.Input.Length] : null;
                if (!DeviceIoControl(device, request.ControlCode, request.Input, request.Input.Length,
                        output, output?.Length ?? 0, out var returned, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"DeviceIoControl failed at operation {index}.");
                }
                if (output is not null && (returned < 0 || returned > output.Length || returned != request.Input.Length
                                           || !output.AsSpan(0, returned).SequenceEqual(request.Input)))
                {
                    throw new InvalidDataException("ECHO response did not match the dispatched bytes.");
                }
            }

            await AppendAsync(journal, new { event_name = "ATTEMPT_COMPLETED", attempt_id = attemptId,
                case_id = canonical.CaseId }).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or Win32Exception
                                          or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"BLOCKED: {exception.Message}");
            return 2;
        }
    }

    private static void ValidateDriver(string driverPath, string expectedHash)
    {
        if (!string.Equals(Path.GetFileName(driverPath), DriverFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Driver must be named {DriverFileName}.");
        }

        if (expectedHash.Length != 64 || expectedHash.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Expected driver SHA-256 must contain exactly 64 hexadecimal characters.");
        }

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverPath))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expectedHash)))
        {
            throw new InvalidDataException("Driver hash does not match the controller allowlist.");
        }
    }

    private static async Task AppendAsync(FileStream journal, object entry)
    {
        await JsonSerializer.SerializeAsync(journal, entry).ConfigureAwait(false);
        await journal.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
        await journal.FlushAsync().ConfigureAwait(false);
        journal.Flush(flushToDisk: true);
    }

    private static string Required(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : throw new InvalidDataException($"Missing required option {name}.");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[] input,
        int inputSize, byte[]? output, int outputSize, out int bytesReturned, IntPtr overlapped);
}
