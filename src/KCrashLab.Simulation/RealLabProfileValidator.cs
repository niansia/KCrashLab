using System.Text.RegularExpressions;
using KCrashLab.Contracts;

namespace KCrashLab.Simulation;

public static partial class RealLabProfileValidator
{
    private const string RequiredDriver = "KCrashLabTarget.sys";
    private const string RequiredDevice = @"\\.\KCrashLabTarget";
    private const string RequiredInterfaceGuid = "4fd15d37-1f06-4e50-a823-376ad418f196";

    public static RealLabValidationResult Validate(RealLabProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();
        if (profile.SchemaVersion != 1) errors.Add("Only real-lab profile schema_version 1 is supported.");
        if (profile.ExecutionMode != "REAL_LAB") errors.Add("execution_mode must be REAL_LAB.");
        RequireGuid(profile.VmId, "vm_id", errors);
        RequireGuid(profile.CheckpointId, "checkpoint_id", errors);
        var driverHash = profile.DriverSha256 ?? string.Empty;
        if (!Sha256Regex().IsMatch(driverHash) || driverHash.Distinct().Count() == 1) errors.Add("driver_sha256 must be a non-placeholder 64-character lowercase SHA-256.");
        if (!string.Equals(profile.DriverFileName, RequiredDriver, StringComparison.OrdinalIgnoreCase)) errors.Add($"driver_file_name must be {RequiredDriver}.");
        if (!string.Equals(profile.DevicePath, RequiredDevice, StringComparison.Ordinal)) errors.Add($"device_path must be the fixed allowlisted path {RequiredDevice}.");
        if (!string.Equals(profile.DeviceInterfaceGuid, RequiredInterfaceGuid, StringComparison.Ordinal)) errors.Add($"device_interface_guid must be {RequiredInterfaceGuid}.");
        var exchangeRoot = profile.ExchangeRoot ?? string.Empty;
        if (!WindowsAbsolutePathRegex().IsMatch(exchangeRoot) || IsSensitiveRoot(exchangeRoot)) errors.Add("exchange_root must be a dedicated absolute Windows path outside system and user-profile roots.");
        if (!string.Equals((profile.DumpRelativePath ?? string.Empty).Replace('\\', '/'), "dumps/MEMORY.DMP", StringComparison.Ordinal)) errors.Add("dump_relative_path must be dumps/MEMORY.DMP.");
        var authorizationId = profile.AuthorizationId ?? string.Empty;
        if (!AuthorizationRegex().IsMatch(authorizationId) || authorizationId.StartsWith("replace", StringComparison.OrdinalIgnoreCase)) errors.Add("authorization_id must contain a non-placeholder 8-128 character private lab authorization reference.");
        if (string.IsNullOrWhiteSpace(profile.GuestBuild) || profile.GuestBuild?.StartsWith("REPLACE_", StringComparison.Ordinal) != false) errors.Add("guest_build must identify the pinned Windows guest build.");
        if (!profile.DisposableLab) errors.Add("disposable_lab must be true.");
        if (!profile.NetworkIsolated) errors.Add("network_isolated must be true.");
        if (!profile.ImmutableCheckpoint) errors.Add("immutable_checkpoint must be true.");
        if (profile.HeartbeatTimeoutSeconds is < 5 or > 300) errors.Add("heartbeat_timeout_seconds must be between 5 and 300.");
        if (profile.DumpStabilitySeconds is < 5 or > 600) errors.Add("dump_stability_seconds must be between 5 and 600.");
        return new RealLabValidationResult(errors.Count == 0, errors);
    }

    private static void RequireGuid(string? value, string name, ICollection<string> errors)
    {
        if (!Guid.TryParseExact(value, "D", out _) || value != value?.ToLowerInvariant()) errors.Add($"{name} must be a lowercase canonical GUID.");
    }

    private static bool IsSensitiveRoot(string path)
    {
        var normalized = path.TrimEnd('\\').ToLowerInvariant();
        return normalized is "c:" or "c:\\windows" or "c:\\program files" or "c:\\program files (x86)" or "c:\\users"
            || normalized.StartsWith("c:\\windows\\", StringComparison.Ordinal)
            || normalized.StartsWith("c:\\users\\", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("^[A-Za-z]:\\\\[^:*?\"<>|]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex("^[A-Za-z0-9._-]{8,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationRegex();
}

public enum DumpReadiness
{
    Missing,
    Growing,
    Stable,
    Rejected
}

public sealed class DumpStabilityTracker
{
    private readonly TimeSpan requiredStableDuration;
    private long? lastLength;
    private DateTimeOffset? unchangedSince;

    public DumpStabilityTracker(TimeSpan requiredStableDuration)
    {
        if (requiredStableDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requiredStableDuration));
        this.requiredStableDuration = requiredStableDuration;
    }

    public DumpReadiness Observe(DateTimeOffset observedAt, long? length, bool exclusiveReadSucceeded)
    {
        if (length is null) { lastLength = null; unchangedSince = null; return DumpReadiness.Missing; }
        if (length <= 0) return DumpReadiness.Rejected;
        if (length != lastLength) { lastLength = length; unchangedSince = observedAt; return DumpReadiness.Growing; }
        if (!exclusiveReadSucceeded) { unchangedSince = observedAt; return DumpReadiness.Growing; }
        return observedAt - unchangedSince >= requiredStableDuration ? DumpReadiness.Stable : DumpReadiness.Growing;
    }
}
