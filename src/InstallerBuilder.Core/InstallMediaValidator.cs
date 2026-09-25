namespace InstallerBuilder.Core;

public enum CheckLevel
{
    Ok,
    Warning,
    Error,
}

public sealed record MediaCheck(CheckLevel Level, string Message);

/// <summary>Egy meglévő telepítő (pendrive / kicsomagolt ISO) ellenőrzése, mielőtt ráírunk.</summary>
public static class InstallMediaValidator
{
    public const long Fat32MaxFileSize = 4L * 1024 * 1024 * 1024 - 1;

    public static IReadOnlyList<MediaCheck> Check(string root, IReadOnlyList<PlannedFile> plan, string? fileSystem, long? availableBytes)
    {
        var checks = new List<MediaCheck>();
        var image = FindInstallImage(root);
        var hasSetup = File.Exists(Path.Combine(root, "setup.exe")) || Directory.EnumerateFiles(root).Any(f => Path.GetFileName(f).Equals("setup.exe", StringComparison.OrdinalIgnoreCase));
        if (image is null || !hasSetup)
        {
            checks.Add(new(CheckLevel.Error, "Ez nem Windows telepítő: hiányzik a setup.exe vagy a sources\\install.wim / install.esd / install.swm."));
        }
        else
        {
            checks.Add(new(CheckLevel.Ok, $"Windows telepítő található (sources\\{image})"));
        }

        var need = MediaPlanner.TotalBytes(plan);
        if (availableBytes is { } free)
        {
            checks.Add(free > need + 50L * 1024 * 1024
                ? new(CheckLevel.Ok, $"Szabad hely: {MediaWriter.FormatSize(free)} (kell: {MediaWriter.FormatSize(need)})")
                : new(CheckLevel.Error, $"Nincs elég hely: {MediaWriter.FormatSize(free)} szabad, {MediaWriter.FormatSize(need)} kell."));
        }

        checks.AddRange(CheckFileSystem(plan, fileSystem));

        if (File.Exists(Path.Combine(root, MediaLayout.AutounattendFile)))
        {
            checks.Add(new(CheckLevel.Warning, "Már van rajta autounattend.xml – biztonsági másolat készül róla (autounattend.xml.bak)."));
        }
        return checks;
    }

    public static IEnumerable<MediaCheck> CheckFileSystem(IReadOnlyList<PlannedFile> plan, string? fileSystem)
    {
        if (!string.Equals(fileSystem, "FAT32", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }
        var tooBig = plan.Where(f => f.Length > Fat32MaxFileSize).ToList();
        if (tooBig.Count == 0)
        {
            yield return new(CheckLevel.Ok, "Minden telepítő 4 GB alatt (FAT32)");
        }
        else
        {
            yield return new(CheckLevel.Error,
                "FAT32-n 4 GB-nál nagyobb fájl nem fér el: " + string.Join(", ", tooBig.Select(f => Path.GetFileName(f.SourcePath ?? f.RelativePath))));
        }
    }

    /// <summary>A sources mappa telepítő-képfájlja (install.wim / .esd / .swm), vagy null.</summary>
    public static string? FindInstallImage(string root)
    {
        var sources = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).FirstOrDefault(d => Path.GetFileName(d).Equals("sources", StringComparison.OrdinalIgnoreCase))
            : null;
        if (sources is null)
        {
            return null;
        }
        return Directory.EnumerateFiles(sources)
            .Select(Path.GetFileName)
            .FirstOrDefault(n => n is not null && (n.Equals("install.wim", StringComparison.OrdinalIgnoreCase)
                || n.Equals("install.esd", StringComparison.OrdinalIgnoreCase)
                || n.Equals("install.swm", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool HasNoErrors(IEnumerable<MediaCheck> checks) => checks.All(c => c.Level != CheckLevel.Error);
}
