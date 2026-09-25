using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace InstallerBuilder.Core;

/// <summary>
/// Az autounattend.xml előállítása. A Windows telepítő a telepítő adathordozó gyökerében lévő autounattend.xml-t
/// magától felhasználja.
///
/// SZÁNDÉKOSAN NEM automatizált: a lemez kiválasztása / formázása (egy rossz lemez letörlése nem visszafordítható)
/// és a termékkulcs / kiadás kiválasztása - ezeket a telepítő továbbra is megkérdezi.
///
/// A helyi felhasználót NEM a szokásos &lt;LocalAccount&gt; elem hozza létre: annak &lt;Group&gt; eleme a csoport
/// HELYI nevét várja ("Administrators" helyett magyar Windowson "Rendszergazdák"), különben a fiók létrehozása
/// elbukhat. Helyette a specialize fázisban a Specialize.ps1 hozza létre, és a rendszergazda csoportot a nyelvtől
/// független SID-jével (S-1-5-32-544) adja meg.
/// </summary>
public static class AutounattendGenerator
{
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";

    public const string Architecture = "amd64";

    public static string Generate(BuildProfile profile)
    {
        var w = profile.Windows;
        var hasPrograms = profile.Programs.Count > 0;
        var createUser = w.CreateLocalUser && !string.IsNullOrWhiteSpace(w.UserName);

        var root = new XElement(Ns + "unattend",
            new XAttribute(XNamespace.Xmlns + "wcm", Wcm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"));

        // ---- windowsPE: a telepítő nyelve, licencszerződés ----
        var pe = Pass("windowsPE");
        if (w.ApplyRegionalSettings)
        {
            pe.Add(Component("Microsoft-Windows-International-Core-WinPE",
                new XElement(Ns + "SetupUILanguage", El("UILanguage", w.UiLanguage)),
                El("InputLocale", w.InputLocale),
                El("SystemLocale", w.SystemLocale),
                El("UILanguage", w.UiLanguage),
                El("UserLocale", w.UserLocale)));
        }
        if (w.SkipOobeQuestions)
        {
            pe.Add(Component("Microsoft-Windows-Setup",
                new XElement(Ns + "UserData", El("AcceptEula", "true"))));
        }
        root.Add(pe);

        // ---- specialize: gépnév, időzóna, a mappánk biztosítása, a felhasználó létrehozása ----
        var specialize = Pass("specialize");
        var shellSpecialize = new List<object>();
        if (!string.IsNullOrWhiteSpace(w.ComputerName))
        {
            shellSpecialize.Add(El("ComputerName", w.ComputerName!.Trim()));
        }
        if (w.ApplyRegionalSettings)
        {
            shellSpecialize.Add(El("TimeZone", w.TimeZone));
        }
        if (shellSpecialize.Count > 0)
        {
            specialize.Add(Component("Microsoft-Windows-Shell-Setup", shellSpecialize.ToArray()));
        }
        var commands = new List<XElement>();
        if (hasPrograms || createUser)
        {
            // Tartalék: ha a telepítő valamiért nem másolta volna át a sources\$OEM$ mappát, a meghajtókon megkeressük.
            commands.Add(RunSynchronous(commands.Count + 1, FallbackCopyCommand(), "ProgramTelepito mappa biztositasa"));
        }
        if (createUser)
        {
            commands.Add(RunSynchronous(commands.Count + 1,
                $@"cmd.exe /c powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""{MediaLayout.InstalledFolderCmd}\{MediaLayout.SpecializeScript}""",
                "Helyi felhasznalo letrehozasa"));
        }
        if (commands.Count > 0)
        {
            specialize.Add(Component("Microsoft-Windows-Deployment", new XElement(Ns + "RunSynchronous", commands)));
        }
        root.Add(specialize);

        // ---- oobeSystem: régió, kérdések, automatikus bejelentkezés, a programválasztó indítása ----
        var oobe = Pass("oobeSystem");
        if (w.ApplyRegionalSettings)
        {
            oobe.Add(Component("Microsoft-Windows-International-Core",
                El("InputLocale", w.InputLocale),
                El("SystemLocale", w.SystemLocale),
                El("UILanguage", w.UiLanguage),
                El("UserLocale", w.UserLocale)));
        }

        // A Windows SIM is ábécérendben írja ezeket - ugyanígy tartjuk.
        var shell = new List<object>();
        if (createUser && w.AutoLogonOnce)
        {
            shell.Add(new XElement(Ns + "AutoLogon",
                new XElement(Ns + "Password",
                    El("Value", PasswordEncoder.Encode(w.Password ?? "")),
                    El("PlainText", "false")),
                El("Enabled", "true"),
                El("LogonCount", "1"),
                El("Username", w.UserName.Trim())));
        }
        if (hasPrograms)
        {
            shell.Add(new XElement(Ns + "FirstLogonCommands",
                new XElement(Ns + "SynchronousCommand",
                    new XAttribute(Wcm + "action", "add"),
                    El("CommandLine",
                        $@"cmd.exe /c powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{MediaLayout.InstalledFolderCmd}\{MediaLayout.InstallScript}"""),
                    El("Description", "Programok telepitese"),
                    El("Order", "1"),
                    El("RequiresUserInput", "true"))));
        }
        var oobeElements = new List<object>();
        if (w.SkipOobeQuestions)
        {
            oobeElements.Add(El("HideEULAPage", "true"));
            oobeElements.Add(El("HideOEMRegistrationScreen", "true"));
        }
        if (createUser)
        {
            oobeElements.Add(El("HideLocalAccountScreen", "true"));
            oobeElements.Add(El("HideOnlineAccountScreens", "true"));
        }
        if (oobeElements.Count > 0)
        {
            // A WiFi-oldal SZÁNDÉKOSAN látszik: a winget-es programokhoz internet kell.
            oobeElements.Add(El("HideWirelessSetupInOOBE", "false"));
            if (w.SkipOobeQuestions)
            {
                oobeElements.Add(El("ProtectYourPC", "3"));
            }
            shell.Add(new XElement(Ns + "OOBE", oobeElements));
        }
        if (w.ApplyRegionalSettings)
        {
            shell.Add(El("TimeZone", w.TimeZone));
        }
        if (shell.Count > 0)
        {
            oobe.Add(Component("Microsoft-Windows-Shell-Setup", shell.ToArray()));
        }
        root.Add(oobe);

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(sb), new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            doc.Save(writer);
        }
        return sb.ToString();
    }

    /// <summary>A mappa átmásolása bármelyik meghajtóról, ha még nincs a helyén (cmd /c-ben a %d egyszeres %).</summary>
    public static string FallbackCopyCommand()
    {
        var media = MediaLayout.MediaFolder.Replace('/', '\\');
        var target = MediaLayout.InstalledFolderCmd;
        return $@"cmd.exe /c for %d in (C D E F G H I J K L M N O P Q R S T U V W X Y Z) do if exist ""%d:\{media}\{MediaLayout.InstallScript}"" if not exist ""{target}\{MediaLayout.InstallScript}"" xcopy /e /i /y /q ""%d:\{media}"" ""{target}""";
    }

    private static XElement Pass(string name) => new(Ns + "settings", new XAttribute("pass", name));

    private static XElement Component(string name, params object[] content) =>
        new(Ns + "component",
            new XAttribute("name", name),
            new XAttribute("processorArchitecture", Architecture),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"),
            new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"),
            content);

    private static XElement RunSynchronous(int order, string path, string description) =>
        new(Ns + "RunSynchronousCommand",
            new XAttribute(Wcm + "action", "add"),
            El("Description", description),
            El("Order", order.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            El("Path", path));

    private static XElement El(string name, string value) => new(Ns + name, value);

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter(StringBuilder sb) : base(sb) { }
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
