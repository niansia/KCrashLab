using System.Text.Json;
using KCrashLab.GuestAgent;

namespace KCrashLab.Tests;

public sealed class TrackBContractSyncTests
{
    [Fact]
    public async Task NativeAndManagedIoctlContractsStaySynchronized()
    {
        var header = await File.ReadAllTextAsync(Path.Combine(TestPaths.RepositoryRoot, "drivers", "KCrashLab.Target", "Public.h"));
        Assert.Contains("CTL_CODE(KCL_DEVICE_TYPE, 0x801, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)", header);
        Assert.Contains("CTL_CODE(KCL_DEVICE_TYPE, 0x802, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)", header);
        Assert.Contains("CTL_CODE(KCL_DEVICE_TYPE, 0x803, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)", header);
        Assert.Contains("CTL_CODE(KCL_DEVICE_TYPE, 0x804, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)", header);
        Assert.Equal(GuestCaseCompiler.Echo, CtlCode(0x801));
        Assert.Equal(GuestCaseCompiler.ResetState, CtlCode(0x802));
        Assert.Equal(GuestCaseCompiler.SetMode, CtlCode(0x803));
        Assert.Equal(GuestCaseCompiler.SubmitRecord, CtlCode(0x804));
    }

    [Fact]
    public async Task InterfaceGuidAndDevicePathStayPinnedAcrossDriverAndProfile()
    {
        const string guid = "4fd15d37-1f06-4e50-a823-376ad418f196";
        var driver = (await File.ReadAllTextAsync(Path.Combine(TestPaths.RepositoryRoot, "drivers", "KCrashLab.Target", "Driver.c"))).ToLowerInvariant();
        Assert.Contains("0x4fd15d37", driver);
        using var profile = JsonDocument.Parse(await File.ReadAllBytesAsync(TestPaths.Sample("real-lab-profile.template.json")));
        Assert.Equal(guid, profile.RootElement.GetProperty("device_interface_guid").GetString());
        Assert.Equal(@"\\.\KCrashLabTarget", profile.RootElement.GetProperty("device_path").GetString());
    }

    private static uint CtlCode(uint function) => (0x22u << 16) | (3u << 14) | (function << 2);
}
