using System.Text.Json.Serialization;

namespace InstallerBuilder.Core;

/// <summary>Honnan települ egy program: a winget katalógusból (internet kell) vagy egy saját, a telepítőre másolt fájlból.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgramSource
{
    Winget,
    Local,
}

/// <summary>Egy program a listában.</summary>
public sealed class ProgramEntry
{
    /// <summary>A telepítéskor megjelenő név (pl. "Google Chrome").</summary>
    public string Name { get; set; } = "";

    public ProgramSource Source { get; set; } = ProgramSource.Winget;

    /// <summary>Winget csomag-azonosító (pl. "Google.Chrome") - csak <see cref="ProgramSource.Winget"/> esetén.</summary>
    public string? WingetId { get; set; }

    /// <summary>A saját telepítő teljes útvonala a KÉSZÍTŐ gépen - csak <see cref="ProgramSource.Local"/> esetén.</summary>
    public string? LocalPath { get; set; }

    /// <summary>Csendes telepítés kapcsolói saját telepítőhöz (pl. "/S", "/VERYSILENT /NORESTART"). .msi-nél elhagyható.</summary>
    public string? SilentArgs { get; set; }

    /// <summary>A telepítéskor megjelenő választólistában alapból be van-e pipálva.</summary>
    public bool DefaultSelected { get; set; } = true;
}

/// <summary>A Windows telepítés automatikus beállításai (autounattend.xml).</summary>
public sealed class WindowsSettings
{
    /// <summary>Nyelv és régió (hu-HU). A telepítő adathordozónak is ilyen nyelvűnek kell lennie a felülethez.</summary>
    public bool ApplyRegionalSettings { get; set; } = true;
    public string UiLanguage { get; set; } = "hu-HU";
    public string UserLocale { get; set; } = "hu-HU";
    public string SystemLocale { get; set; } = "hu-HU";
    /// <summary>Billentyűzet (magyar: 040e:0000040e).</summary>
    public string InputLocale { get; set; } = "040e:0000040e";
    public string TimeZone { get; set; } = "Central Europe Standard Time";

    /// <summary>A telepítés eleji és végi (OOBE) kérdések átugrása: licencszerződés, adatvédelem, OEM regisztráció.</summary>
    public bool SkipOobeQuestions { get; set; } = true;

    /// <summary>Helyi (Microsoft-fiók nélküli) rendszergazda felhasználó létrehozása.</summary>
    public bool CreateLocalUser { get; set; } = true;
    public string UserName { get; set; } = "";
    public string? DisplayName { get; set; }
    /// <summary>Üres = jelszó nélküli fiók.</summary>
    public string Password { get; set; } = "";
    /// <summary>Az első indításkor automatikus bejelentkezés (egyszer) - így a programok telepítése magától indul.</summary>
    public bool AutoLogonOnce { get; set; } = true;

    /// <summary>Számítógép neve; üres = a Windows választ.</summary>
    public string? ComputerName { get; set; }
}

/// <summary>A telepítés közben (első bejelentkezéskor) futó programválasztó beállításai.</summary>
public sealed class InstallerSettings
{
    /// <summary>A választólista ennyi másodperc után magától elindítja a telepítést (0 = megvárja a gombnyomást).</summary>
    public int AutoStartSeconds { get; set; }

    /// <summary>A végén automatikus újraindítás.</summary>
    public bool RebootWhenDone { get; set; }
}

/// <summary>Egy teljes, menthető profil (a program "Profil mentése" JSON-ja).</summary>
public sealed class BuildProfile
{
    public List<ProgramEntry> Programs { get; set; } = new();
    public WindowsSettings Windows { get; set; } = new();
    public InstallerSettings Installer { get; set; } = new();
}
