using System.Text;

namespace InstallerBuilder.Core;

/// <summary>Egy lemez adatai a pendrive-választóhoz (a Windows Get-Disk kimenetéből).</summary>
public sealed record DiskInfo(int Number, string FriendlyName, string BusType, long SizeBytes, bool IsSystem, bool IsBoot, IReadOnlyList<string> DriveLetters);

/// <summary>A Windows eszközök (diskpart, dism, bootsect, oscdimg) parancsai - tiszta függvények, hogy tesztelhetők legyenek.</summary>
public static class WindowsMediaCommands
{
    /// <summary>A FAT32 partíció legnagyobb mérete, amit a Windows formázója kezel.</summary>
    public const long MaxFat32PartitionBytes = 32L * 1024 * 1024 * 1024;

    public const long MinUsbBytes = 7L * 1024 * 1024 * 1024;

    /// <summary>
    /// Formázható-e a lemez telepítő pendrive-nak. SZIGORÚ: csak USB buszon lévő, nem rendszer- és nem boot-lemez,
    /// legalább 8 GB (a "8 GB"-os pendrive valódi mérete kicsit kevesebb) és legfeljebb 2 TB.
    /// </summary>
    public static string? WhyNotFormattable(DiskInfo disk)
    {
        if (disk.IsSystem || disk.IsBoot)
        {
            return "Ez a rendszerlemez – nem formázható.";
        }
        if (!disk.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            return $"Nem USB-s lemez ({disk.BusType}) – csak pendrive formázható.";
        }
        if (disk.SizeBytes < MinUsbBytes)
        {
            return "Túl kicsi: legalább 8 GB-os pendrive kell.";
        }
        if (disk.SizeBytes > 2L * 1024 * 1024 * 1024 * 1024)
        {
            return "Túl nagy (2 TB felett) – ez valószínűleg külső merevlemez, nem pendrive.";
        }
        return null;
    }

    /// <summary>
    /// diskpart szkript: a lemez törlése, MBR, egy aktív FAT32 partíció (legfeljebb 32 GB), meghajtóbetűvel.
    /// MBR + aktív FAT32 = UEFI-s és régi BIOS-os gépen is bootol (a BIOS-hoz a bootsect írja a boot-kódot).
    /// </summary>
    public static string DiskpartScript(DiskInfo disk, char driveLetter, string label)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"select disk {disk.Number}");
        sb.AppendLine("clean");
        sb.AppendLine("convert mbr");
        if (disk.SizeBytes > MaxFat32PartitionBytes)
        {
            sb.AppendLine($"create partition primary size={MaxFat32PartitionBytes / (1024 * 1024) - 64}");
        }
        else
        {
            sb.AppendLine("create partition primary");
        }
        sb.AppendLine($"format fs=fat32 quick label=\"{SanitizeLabel(label)}\"");
        sb.AppendLine("active");
        sb.AppendLine($"assign letter={char.ToUpperInvariant(driveLetter)}");
        sb.AppendLine("exit");
        return sb.ToString();
    }

    /// <summary>FAT32 kötetcímke: legfeljebb 11 karakter, nagybetű, csak betű/szám/aláhúzás/kötőjel.</summary>
    public static string SanitizeLabel(string label)
    {
        var chars = label.ToUpperInvariant().Where(c => (c >= 'A' && c <= 'Z') || char.IsDigit(c) || c == '_' || c == '-').ToArray();
        var result = new string(chars);
        if (result.Length == 0)
        {
            result = "WINTELEPITO";
        }
        return result.Length > 11 ? result[..11] : result;
    }

    /// <summary>A 4 GB-nál nagyobb install.wim feldarabolása FAT32-höz (a Windows telepítő az install.swm részeket kezeli).</summary>
    public static string DismSplitArgs(string sourceWim, string targetSwm) =>
        $"/Split-Image /ImageFile:\"{sourceWim}\" /SWMFile:\"{targetSwm}\" /FileSize:3800";

    public static string BootsectArgs(char driveLetter) => $"/nt60 {char.ToUpperInvariant(driveLetter)}: /force /mbr";

    /// <summary>oscdimg: bootolható (BIOS + UEFI) UDF ISO a munkamappából - a Microsoft által dokumentált kapcsolókkal.</summary>
    public static string OscdimgArgs(string workDir, string outputIso, string label)
    {
        var etfs = Path.Combine(workDir, "boot", "etfsboot.com");
        var efi = Path.Combine(workDir, "efi", "microsoft", "boot", "efisys.bin");
        var safeLabel = new string(label.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (safeLabel.Length == 0)
        {
            safeLabel = "WINTELEPITO";
        }
        if (safeLabel.Length > 32)
        {
            safeLabel = safeLabel[..32];
        }
        return $"-m -o -u2 -udfver102 -l{safeLabel} -bootdata:2#p0,e,b\"{etfs}\"#pEF,e,b\"{efi}\" \"{workDir}\" \"{outputIso}\"";
    }

    /// <summary>Az oscdimg.exe szokásos helyei (Windows ADK - Deployment Tools).</summary>
    public static IEnumerable<string> OscdimgCandidates(string programFilesX86, string programFiles)
    {
        foreach (var pf in new[] { programFilesX86, programFiles })
        {
            if (string.IsNullOrEmpty(pf))
            {
                continue;
            }
            foreach (var arch in new[] { "amd64", "x86", "arm64" })
            {
                yield return Path.Combine(pf, "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", arch, "Oscdimg", "oscdimg.exe");
            }
        }
    }
}
