namespace InstallerBuilder.Core;

/// <summary>Az adathordozón és a telepített gépen használt útvonalak - egy helyen, hogy a generátor, az író és a szkriptek egyezzenek.</summary>
public static class MediaLayout
{
    /// <summary>A mappa neve a telepített gépen: %WINDIR%\Setup\Scripts\ProgramTelepito.</summary>
    public const string FolderName = "ProgramTelepito";

    /// <summary>
    /// Az adathordozón: sources\$OEM$\$$\Setup\Scripts\ProgramTelepito. A Windows telepítő a sources\$OEM$\$$ tartalmát a
    /// %WINDIR% mappába másolja (Microsoft által dokumentált mód, pl. a SetupComplete.cmd-hez).
    /// </summary>
    public const string MediaFolder = "sources/$OEM$/$$/Setup/Scripts/" + FolderName;

    /// <summary>A mappa a telepített gépen, cmd-szintaxissal (a %WINDIR% a parancs futásakor bomlik ki).</summary>
    public const string InstalledFolderCmd = @"%WINDIR%\Setup\Scripts\" + FolderName;

    public const string InstallerSubfolder = "telepitok";
    public const string ProgramsJson = "programok.json";
    public const string UserJson = "felhasznalo.json";
    public const string InstallScript = "Install-Programs.ps1";
    public const string SpecializeScript = "Specialize.ps1";
    public const string RerunCmd = "Programok-telepitese.cmd";
    public const string AutounattendFile = "autounattend.xml";
}
