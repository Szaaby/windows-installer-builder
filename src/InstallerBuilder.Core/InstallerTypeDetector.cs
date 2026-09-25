using System.Text;

namespace InstallerBuilder.Core;

public sealed record SilentArgsSuggestion(string InstallerType, string Args, string Note);

/// <summary>
/// Saját telepítőnél kitalálja a csendes telepítés kapcsolóit: .msi-nél a msiexec intézi, .exe-nél a fájlban keresi a
/// gyakori telepítő-készítők nyomát (Inno Setup, NSIS, InstallShield, WiX Burn). Csak javaslat - a felületen átírható.
/// </summary>
public static class InstallerTypeDetector
{
    private static readonly (string Marker, SilentArgsSuggestion Suggestion)[] Markers =
    {
        ("Inno Setup", new("Inno Setup", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-", "Inno Setup telepítő")),
        ("Nullsoft", new("NSIS", "/S", "NSIS telepítő (a /S nagybetűs)")),
        ("NSIS Error", new("NSIS", "/S", "NSIS telepítő (a /S nagybetűs)")),
        ("InstallShield", new("InstallShield", "/s /v\"/qn /norestart\"", "InstallShield telepítő")),
        ("WixBundle", new("WiX Burn", "/quiet /norestart", "WiX (Burn) telepítő")),
        (".wixburn", new("WiX Burn", "/quiet /norestart", "WiX (Burn) telepítő")),
        ("Squirrel", new("Squirrel", "--silent", "Squirrel telepítő (felhasználói telepítés)")),
    };

    public static SilentArgsSuggestion Suggest(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".msi":
                return new("MSI", "", "MSI csomag – a /qn /norestart kapcsolót a telepítő magától adja hozzá");
            case ".msix":
            case ".msixbundle":
            case ".appx":
            case ".appxbundle":
                return new("MSIX", "", "Windows alkalmazáscsomag – kapcsoló nem kell");
            case ".reg":
                return new("REG", "", "Beállításjegyzék-fájl – csendben importálódik");
            case ".cmd":
            case ".bat":
            case ".ps1":
                return new("Szkript", "", "Szkript – változtatás nélkül fut le");
        }
        try
        {
            var text = ReadHead(path, 8 * 1024 * 1024);
            foreach (var (marker, suggestion) in Markers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    return suggestion;
                }
            }
        }
        catch (IOException)
        {
        }
        return new("Ismeretlen", "", "Ismeretlen telepítő – nézd meg a gyártó oldalán a csendes telepítés kapcsolóját (gyakori: /S, /silent, /quiet)");
    }

    private static string ReadHead(string path, int maxBytes)
    {
        using var fs = File.OpenRead(path);
        var buffer = new byte[Math.Min(maxBytes, fs.Length)];
        var read = fs.Read(buffer, 0, buffer.Length);
        // Latin1: minden bájt egy karakter - az ASCII jelölők így megtalálhatók a bináris fájlban.
        return Encoding.Latin1.GetString(buffer, 0, read);
    }
}
