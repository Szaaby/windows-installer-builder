namespace InstallerBuilder.Core;

/// <summary>
/// A terv kiírása egy telepítő gyökérmappájába (pendrive gyökere, ISO munkamappa vagy egy sima mappa). Egy már ott
/// lévő autounattend.xml-ről biztonsági másolat készül; a korábbi ProgramTelepito mappa törlődik, hogy ne maradjon benne
/// egy régebbi készítés telepítője.
/// </summary>
public static class MediaWriter
{
    public static void Write(IReadOnlyList<PlannedFile> plan, string root, IProgress<string>? log = null, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(root);
        var existingUnattend = Path.Combine(root, MediaLayout.AutounattendFile);
        if (File.Exists(existingUnattend))
        {
            var backup = existingUnattend + ".bak";
            if (File.Exists(backup))
            {
                backup = $"{existingUnattend}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            }
            File.Move(existingUnattend, backup);
            log?.Report($"A meglévő autounattend.xml másolata: {Path.GetFileName(backup)}");
        }
        var ourFolder = ToLocal(root, MediaLayout.MediaFolder);
        if (Directory.Exists(ourFolder))
        {
            Directory.Delete(ourFolder, recursive: true);
        }

        var total = MediaPlanner.TotalBytes(plan);
        long done = 0;
        var buffer = new byte[1 << 20];
        foreach (var file in plan)
        {
            ct.ThrowIfCancellationRequested();
            var target = ToLocal(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (file.Content is not null)
            {
                File.WriteAllBytes(target, file.Content);
                done += file.Content.LongLength;
                log?.Report($"Megírva: {file.RelativePath}");
            }
            else
            {
                log?.Report($"Másolás: {Path.GetFileName(file.SourcePath)} ({FormatSize(file.Length)})");
                using var source = File.OpenRead(file.SourcePath!);
                using var dest = File.Create(target);
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dest.Write(buffer, 0, read);
                    done += read;
                    progress?.Report((done, total));
                }
            }
            progress?.Report((done, total));
        }
    }

    public static string ToLocal(string root, string relative) =>
        Path.Combine(new[] { root }.Concat(relative.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray());

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} kB",
        _ => $"{bytes} B",
    };
}
