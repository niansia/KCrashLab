using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Microsoft.Win32;
using KCrashLab.Contracts;

namespace KCrashLab.Simulation;

public sealed class HostCapabilityProbe
{
    public static CapabilityReport Probe(DateTimeOffset observedAtUtc)
    {
        var evidence = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var edition = ReadWindowsEdition(evidence);
        var hypervisor = ProbeHypervisor(evidence);
        var hypervManagement = ProbeHyperVManagement(evidence);
        var (sdk, wdk) = ProbeWindowsKits(evidence);
        var reasons = new List<string>();

        if (hypervManagement != CapabilityStatus.Available)
        {
            reasons.Add("Hyper-V management module is unavailable or unverified.");
        }

        if (wdk != CapabilityStatus.Available)
        {
            reasons.Add("WDK driver build targets are unavailable or unverified.");
        }

        reasons.Add("No disposable kernel lab is configured for v1.");

        return new CapabilityReport(
            1,
            "SIMULATED",
            observedAtUtc,
            new HostDescription(
                RuntimeInformation.OSDescription,
                Environment.OSVersion.Version.ToString(),
                RuntimeInformation.OSArchitecture.ToString(),
                edition),
            new CapabilitySet(
                hypervisor,
                hypervManagement,
                sdk,
                wdk,
                CapabilityStatus.Unavailable),
            CapabilityStatus.Blocked,
            reasons,
            evidence);
    }

    private static string? ReadWindowsEdition(SortedDictionary<string, string> evidence)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string;
        var edition = key?.GetValue("EditionID") as string;
        if (product is not null)
        {
            evidence["windows_product_name"] = product;
        }

        if (edition is not null)
        {
            evidence["windows_edition_id"] = edition;
        }

        if (Environment.OSVersion.Version.Build >= 22_000)
        {
            return edition switch
            {
                "Core" => "Windows 11 Home",
                "CoreSingleLanguage" => "Windows 11 Home Single Language",
                "Professional" => "Windows 11 Pro",
                "Enterprise" => "Windows 11 Enterprise",
                "Education" => "Windows 11 Education",
                _ => product is null ? edition : edition is null ? product : $"{product} ({edition})"
            };
        }

        return product is null ? edition : edition is null ? product : $"{product} ({edition})";
    }

    private static CapabilityStatus ProbeHypervisor(SortedDictionary<string, string> evidence)
    {
        if (!X86Base.IsSupported)
        {
            evidence["hypervisor_probe"] = "CPUID unavailable on this architecture";
            return CapabilityStatus.Unverified;
        }

        var (_, _, ecx, _) = X86Base.CpuId(1, 0);
        var present = (ecx & unchecked((int)0x80000000)) != 0;
        evidence["hypervisor_cpuid_bit"] = present ? "1" : "0";
        return present ? CapabilityStatus.Available : CapabilityStatus.Unavailable;
    }

    private static CapabilityStatus ProbeHyperVManagement(SortedDictionary<string, string> evidence)
    {
        if (!OperatingSystem.IsWindows())
        {
            return CapabilityStatus.Unavailable;
        }

        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "Modules", "Hyper-V", "Hyper-V.psd1")
        };
        var modulePath = Environment.GetEnvironmentVariable("PSModulePath");
        if (!string.IsNullOrWhiteSpace(modulePath))
        {
            candidates.AddRange(modulePath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(static path => Path.Combine(path, "Hyper-V", "Hyper-V.psd1")));
        }

        var found = candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(File.Exists);
        evidence["hyperv_module_manifest"] = found ?? "not found";
        return found is null ? CapabilityStatus.Unavailable : CapabilityStatus.Available;
    }

    private static (CapabilityStatus Sdk, CapabilityStatus Wdk) ProbeWindowsKits(SortedDictionary<string, string> evidence)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (CapabilityStatus.Unavailable, CapabilityStatus.Unavailable);
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
        var root = key?.GetValue("KitsRoot10") as string;
        evidence["windows_kits_root"] = root ?? "not found";
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return (CapabilityStatus.Unavailable, CapabilityStatus.Unavailable);
        }

        var includeRoot = Path.Combine(root, "Include");
        var versions = Directory.Exists(includeRoot)
            ? Directory.EnumerateDirectories(includeRoot).Select(Path.GetFileName).Where(static name => name is not null).Cast<string>().Order(StringComparer.Ordinal).ToArray()
            : [];
        evidence["windows_sdk_include_versions"] = versions.Length == 0 ? "none" : string.Join(',', versions);
        var sdk = versions.Length == 0 ? CapabilityStatus.Unavailable : CapabilityStatus.Available;

        var buildRoot = Path.Combine(root, "build");
        var driverTargets = Directory.Exists(buildRoot)
            ? Directory.EnumerateFiles(buildRoot, "WindowsDriver.common.targets", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        evidence["wdk_driver_targets"] = driverTargets ?? "not found";
        var wdk = driverTargets is null ? CapabilityStatus.Unavailable : CapabilityStatus.Available;
        return (sdk, wdk);
    }
}
