using System.Text;
using System.Xml.Linq;

namespace InstallerBuilder.Core.Tests;

public class AutounattendTests
{
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";

    private static XDocument Gen(BuildProfile p) => XDocument.Parse(AutounattendGenerator.Generate(p));

    private static XElement Pass(XDocument d, string name) =>
        d.Root!.Elements(Ns + "settings").Single(s => (string?)s.Attribute("pass") == name);

    private static XElement Component(XElement pass, string name) =>
        pass.Elements(Ns + "component").Single(c => (string?)c.Attribute("name") == name);

    [Fact]
    public void Password_encoding_matches_windows_format()
    {
        var expected = Convert.ToBase64String(Encoding.Unicode.GetBytes("titkos123Password"));
        Assert.Equal(expected, PasswordEncoder.Encode("titkos123"));
        Assert.Equal("UABhAHMAcwB3AG8AcgBkAA==", PasswordEncoder.Encode(""));
    }

    [Fact]
    public void Full_profile_generates_all_passes()
    {
        var d = Gen(Profiles.Sample());
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", AutounattendGenerator.Generate(Profiles.Sample()));

        var pe = Pass(d, "windowsPE");
        var intl = Component(pe, "Microsoft-Windows-International-Core-WinPE");
        Assert.Equal("hu-HU", intl.Element(Ns + "UILanguage")!.Value);
        Assert.Equal("040e:0000040e", intl.Element(Ns + "InputLocale")!.Value);
        Assert.Equal("true", Component(pe, "Microsoft-Windows-Setup").Descendants(Ns + "AcceptEula").Single().Value);
        // A lemez és a kiadás kiválasztása SZÁNDÉKOSAN nincs automatizálva.
        Assert.Empty(d.Descendants(Ns + "DiskConfiguration"));
        Assert.Empty(d.Descendants(Ns + "ProductKey"));
        Assert.Empty(d.Descendants(Ns + "LocalAccounts"));

        var spec = Pass(d, "specialize");
        Assert.Equal("SZABI-PC", Component(spec, "Microsoft-Windows-Shell-Setup").Element(Ns + "ComputerName")!.Value);
        var cmds = spec.Descendants(Ns + "RunSynchronousCommand").ToList();
        Assert.Equal(2, cmds.Count);
        Assert.Contains("xcopy", cmds[0].Element(Ns + "Path")!.Value);
        Assert.Contains(@"%WINDIR%\Setup\Scripts\ProgramTelepito\Specialize.ps1", cmds[1].Element(Ns + "Path")!.Value);
        Assert.Equal(new[] { "1", "2" }, cmds.Select(c => c.Element(Ns + "Order")!.Value));

        var oobe = Pass(d, "oobeSystem");
        var shell = Component(oobe, "Microsoft-Windows-Shell-Setup");
        var autoLogon = shell.Element(Ns + "AutoLogon")!;
        Assert.Equal("Szabi", autoLogon.Element(Ns + "Username")!.Value);
        Assert.Equal("1", autoLogon.Element(Ns + "LogonCount")!.Value);
        Assert.Equal(PasswordEncoder.Encode("titkos123"), autoLogon.Descendants(Ns + "Value").Single().Value);
        Assert.Equal("false", autoLogon.Descendants(Ns + "PlainText").Single().Value);
        Assert.DoesNotContain("titkos123", AutounattendGenerator.Generate(Profiles.Sample()));

        var first = shell.Descendants(Ns + "SynchronousCommand").Single();
        Assert.Contains(@"%WINDIR%\Setup\Scripts\ProgramTelepito\Install-Programs.ps1", first.Element(Ns + "CommandLine")!.Value);
        Assert.Equal("true", first.Element(Ns + "RequiresUserInput")!.Value);

        var o = shell.Element(Ns + "OOBE")!;
        Assert.Equal("true", o.Element(Ns + "HideOnlineAccountScreens")!.Value);
        Assert.Equal("true", o.Element(Ns + "HideLocalAccountScreen")!.Value);
        Assert.Equal("false", o.Element(Ns + "HideWirelessSetupInOOBE")!.Value);
        Assert.Equal("3", o.Element(Ns + "ProtectYourPC")!.Value);
        Assert.Equal("Central Europe Standard Time", shell.Element(Ns + "TimeZone")!.Value);

        // Minden komponens amd64, a szabványos azonosítókkal.
        foreach (var c in d.Descendants(Ns + "component"))
        {
            Assert.Equal("amd64", (string?)c.Attribute("processorArchitecture"));
            Assert.Equal("31bf3856ad364e35", (string?)c.Attribute("publicKeyToken"));
        }
    }

    [Fact]
    public void Minimal_profile_only_regional_settings()
    {
        var p = new BuildProfile();
        p.Windows.CreateLocalUser = false;
        p.Windows.SkipOobeQuestions = false;
        var d = Gen(p);
        Assert.Empty(d.Descendants(Ns + "AutoLogon"));
        Assert.Empty(d.Descendants(Ns + "FirstLogonCommands"));
        Assert.Empty(d.Descendants(Ns + "RunSynchronous"));
        Assert.Empty(d.Descendants(Ns + "AcceptEula"));
        Assert.Empty(d.Descendants(Ns + "OOBE"));
        Assert.Single(d.Descendants(Ns + "SetupUILanguage"));
    }

    [Fact]
    public void Programs_without_user_still_start_installer()
    {
        var p = Profiles.Sample();
        p.Windows.CreateLocalUser = false;
        var d = Gen(p);
        Assert.Single(d.Descendants(Ns + "FirstLogonCommands"));
        Assert.Empty(d.Descendants(Ns + "AutoLogon"));
        Assert.Single(d.Descendants(Ns + "RunSynchronousCommand")); // csak a tartalék-másolás
        Assert.Empty(d.Descendants(Ns + "HideOnlineAccountScreens"));
    }

    [Fact]
    public void Fallback_copy_command_uses_single_percent_and_media_path()
    {
        var cmd = AutounattendGenerator.FallbackCopyCommand();
        Assert.Contains(@"%d:\sources\$OEM$\$$\Setup\Scripts\ProgramTelepito", cmd);
        Assert.DoesNotContain("%%d", cmd);
        Assert.StartsWith("cmd.exe /c for %d in (C D E", cmd);
    }
}
