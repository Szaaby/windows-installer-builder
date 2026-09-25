namespace InstallerBuilder.Core.Tests;

public class WindowsCommandsTests
{
    private static DiskInfo Usb(long gb, int n = 2) => new(n, "SanDisk Ultra", "USB", gb * 1024 * 1024 * 1024, false, false, new[] { "E" });

    [Fact]
    public void Only_usb_non_system_disks_are_formattable()
    {
        Assert.Null(WindowsMediaCommands.WhyNotFormattable(Usb(32)));
        Assert.Contains("rendszerlemez", WindowsMediaCommands.WhyNotFormattable(Usb(32) with { IsSystem = true }));
        Assert.Contains("rendszerlemez", WindowsMediaCommands.WhyNotFormattable(Usb(32) with { IsBoot = true }));
        Assert.Contains("Nem USB", WindowsMediaCommands.WhyNotFormattable(Usb(500) with { BusType = "NVMe" }));
        Assert.Contains("Túl kicsi", WindowsMediaCommands.WhyNotFormattable(Usb(4)));
        Assert.Contains("Túl nagy", WindowsMediaCommands.WhyNotFormattable(Usb(4000)));
    }

    [Fact]
    public void Diskpart_script_limits_fat32_partition()
    {
        var big = WindowsMediaCommands.DiskpartScript(Usb(64, 3), 'e', "Win 11 telepítő!");
        Assert.Contains("select disk 3", big);
        Assert.Contains("clean", big);
        Assert.Contains("convert mbr", big);
        Assert.Contains("create partition primary size=32704", big);
        Assert.Contains("format fs=fat32 quick label=\"WIN11TELEPT\"", big);
        Assert.Contains("active", big);
        Assert.Contains("assign letter=E", big);

        var small = WindowsMediaCommands.DiskpartScript(Usb(16), 'F', "WIN11");
        Assert.Contains("create partition primary" + Environment.NewLine, small);
    }

    [Fact]
    public void Tool_arguments()
    {
        Assert.Equal("/nt60 E: /force /mbr", WindowsMediaCommands.BootsectArgs('e'));
        Assert.Equal("/Split-Image /ImageFile:\"X:\\sources\\install.wim\" /SWMFile:\"E:\\sources\\install.swm\" /FileSize:3800",
            WindowsMediaCommands.DismSplitArgs("X:\\sources\\install.wim", "E:\\sources\\install.swm"));
        var osc = WindowsMediaCommands.OscdimgArgs("/w", "/out.iso", "CCCOMA X64FRE HU-HU");
        Assert.StartsWith("-m -o -u2 -udfver102 -lCCCOMAX64FREHUHU -bootdata:2#p0,e,b\"", osc);
        Assert.Contains("etfsboot.com\"#pEF,e,b\"", osc);
        Assert.EndsWith("\"/w\" \"/out.iso\"", osc);
        Assert.Contains(WindowsMediaCommands.OscdimgCandidates(@"C:\Program Files (x86)", @"C:\Program Files"),
            p => p.Contains("Deployment Tools") && p.EndsWith("oscdimg.exe"));
    }
}
