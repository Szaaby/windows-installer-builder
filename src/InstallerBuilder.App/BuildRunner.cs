using System.Text.RegularExpressions;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

public enum SourceMode
{
    Download,
    IsoFile,
    ExistingUsb,
}

public enum TargetMode
{
    Usb,
    Iso,
    Folder,
}

/// <summary>A Készítés fül beállításai egy futáshoz.</summary>
public sealed class BuildRequest
{
    public SourceMode Source { get; init; }
    public WindowsVersion Version { get; init; } = WindowsVersion.Windows11;
    public string Language { get; init; } = "Hungarian";
    public string? IsoPath { get; init; }

    public TargetMode Target { get; init; }
    /// <summary>Friss pendrive-nál a formázandó lemez.</summary>
    public DiskInfo? Disk { get; init; }
    /// <summary>Kész pendrive-nál (ExistingUsb) a gyökere, pl. "E:\".</summary>
    public string? ExistingRoot { get; init; }
    public string? OutputIso { get; init; }
    public string? OscdimgPath { get; init; }
    public string? OutputFolder { get; init; }
}

public enum StepState
{
    Waiting,
    Running,
    Done,
    Failed,
    Skipped,
}

/// <summary>A folyamat-lista egy sora.</summary>
public sealed record BuildStep(string Key, string Title);

/// <summary>
/// A készítés lépésenként. A felület a <see cref="StepChanged"/>, <see cref="Log"/> és <see cref="Overall"/> eseményekből
/// frissül (a hívó szálon - a felület ezeket Invoke-kal a saját szálára teszi).
/// </summary>
public sealed class BuildRunner
{
    private readonly BuildProfile _profile;
    private readonly BuildRequest _request;
    private readonly HttpClient _http;

    public event Action<string, StepState, string?>? StepChanged;
    public event Action<string>? Log;
    public event Action<int>? Overall;

    public BuildRunner(BuildProfile profile, BuildRequest request, HttpClient http)
    {
        _profile = profile;
        _request = request;
        _http = http;
    }

    /// <summary>A futás lépései a választott forrás/cél szerint (a felület ebből rajzolja a listát).</summary>
    public static List<BuildStep> PlanSteps(BuildRequest r)
    {
        var steps = new List<BuildStep>();
        if (r.Target == TargetMode.Folder)
        {
            steps.Add(new("files", "autounattend.xml, programválasztó, telepítők"));
            return steps;
        }
        if (r.Source == SourceMode.ExistingUsb)
        {
            steps.Add(new("check", "A pendrive ellenőrzése"));
            steps.Add(new("files", "autounattend.xml, programválasztó, telepítők"));
            return steps;
        }
        if (r.Source == SourceMode.Download)
        {
            steps.Add(new("download", $"{(r.Version == WindowsVersion.Windows11 ? "Windows 11" : "Windows 10")} letöltése"));
        }
        steps.Add(new("mount", "Az ISO megnyitása"));
        if (r.Target == TargetMode.Usb)
        {
            steps.Add(new("format", "Pendrive formázása"));
            steps.Add(new("copy", "Windows fájlok másolása"));
            steps.Add(new("split", "install.wim darabolása (FAT32 4 GB korlát)"));
            steps.Add(new("boot", "Rendszerindító kód (régi BIOS-hoz)"));
        }
        else
        {
            steps.Add(new("copy", "Windows fájlok másolása munkamappába"));
        }
        steps.Add(new("files", "autounattend.xml, programválasztó, telepítők"));
        if (r.Target == TargetMode.Iso)
        {
            steps.Add(new("iso", "ISO fájl készítése (oscdimg)"));
        }
        steps.Add(new("verify", "Ellenőrzés"));
        return steps;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var plan = MediaPlanner.Plan(_profile);
        var r = _request;

        if (r.Target == TargetMode.Folder)
        {
            await Step("files", () => { WritePlan(plan, r.OutputFolder!, ct); return Task.CompletedTask; });
            Overall?.Invoke(100);
            Log?.Invoke($"Kész: {r.OutputFolder}. A tartalmát egy Windows telepítő pendrive gyökerébe kell másolni.");
            return;
        }

        if (r.Source == SourceMode.ExistingUsb)
        {
            await Step("check", () =>
            {
                var drive = new DriveInfo(r.ExistingRoot!);
                var checks = InstallMediaValidator.Check(r.ExistingRoot!, plan, drive.DriveFormat, drive.AvailableFreeSpace);
                foreach (var c in checks)
                {
                    Log?.Invoke((c.Level == CheckLevel.Ok ? "✓ " : c.Level == CheckLevel.Warning ? "! " : "✗ ") + c.Message);
                }
                if (!InstallMediaValidator.HasNoErrors(checks))
                {
                    throw new InvalidOperationException(checks.First(c => c.Level == CheckLevel.Error).Message);
                }
                return Task.CompletedTask;
            });
            await Step("files", () => { WritePlan(plan, r.ExistingRoot!, ct); return Task.CompletedTask; });
            Overall?.Invoke(100);
            Log?.Invoke("Kész. Dugd a pendrive-ot az új gépbe, és indíts róla.");
            return;
        }

        // ---- ISO forrásból: letöltés (ha kell), csatolás ----
        var iso = r.IsoPath;
        if (r.Source == SourceMode.Download)
        {
            await Step("download", async () => iso = await DownloadAsync(ct));
        }
        Overall?.Invoke(35);

        string? isoRoot = null;
        try
        {
            await Step("mount", async () =>
            {
                isoRoot = await WindowsTools.MountIsoAsync(iso!, ct);
                Log?.Invoke($"ISO csatolva: {isoRoot}");
                if (InstallMediaValidator.FindInstallImage(isoRoot) is null)
                {
                    throw new InvalidOperationException("Ez az ISO nem Windows telepítő (nincs sources\\install.wim / install.esd).");
                }
            });

            if (r.Target == TargetMode.Usb)
            {
                await BuildUsbAsync(isoRoot!, plan, ct);
            }
            else
            {
                await BuildIsoAsync(isoRoot!, plan, ct);
            }
        }
        finally
        {
            if (isoRoot is not null)
            {
                await WindowsTools.DismountIsoAsync(iso!);
            }
        }
        Overall?.Invoke(100);
    }

    private async Task<string> DownloadAsync(CancellationToken ct)
    {
        var r = _request;
        var downloader = new MicrosoftIsoDownloader(_http);
        Log?.Invoke("A legújabb kiadás keresése a Microsoftnál…");
        var link = await downloader.GetDownloadLinkAsync(r.Version, r.Language, "x64", ct);
        var folder = WindowsTools.DownloadsFolder();
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, link.FileName);
        var remote = await downloader.GetRemoteLengthAsync(link.Url, ct);
        if (File.Exists(target) && remote is { } len && new FileInfo(target).Length == len)
        {
            Log?.Invoke($"Már le van töltve: {target}");
            return target;
        }
        Log?.Invoke($"Letöltés: {link.FileName}{(remote is { } size ? " · " + MediaWriter.FormatSize(size) : "")}");
        var lastPct = -1;
        await downloader.DownloadFileAsync(link.Url, target, new Progress<(long Done, long? Total)>(p =>
        {
            if (p.Total is { } total && total > 0)
            {
                var pct = (int)(p.Done * 100 / total);
                if (pct != lastPct)
                {
                    lastPct = pct;
                    StepChanged?.Invoke("download", StepState.Running, $"{pct}% · {MediaWriter.FormatSize(p.Done)} / {MediaWriter.FormatSize(total)}");
                    Overall?.Invoke(pct * 35 / 100);
                }
            }
        }), ct);
        Log?.Invoke($"Letöltve: {target}");
        return target;
    }

    private async Task BuildUsbAsync(string isoRoot, IReadOnlyList<PlannedFile> plan, CancellationToken ct)
    {
        var disk = _request.Disk!;
        char letter = disk.DriveLetters.FirstOrDefault()?[0] ?? WindowsTools.FreeDriveLetter();
        var root = $"{letter}:\\";

        await Step("format", async () =>
        {
            // Közvetlenül a formázás előtt újra ellenőrizzük: ugyanaz a lemez van-e ezen a számon.
            var now = (await WindowsTools.GetDisksAsync(ct)).FirstOrDefault(d => d.Number == disk.Number);
            if (now is null || now.FriendlyName != disk.FriendlyName || now.SizeBytes != disk.SizeBytes)
            {
                throw new InvalidOperationException("A kiválasztott pendrive közben megváltozott (kihúzták?). Frissítsd a listát, és válaszd ki újra.");
            }
            var why = WindowsMediaCommands.WhyNotFormattable(now);
            if (why is not null)
            {
                throw new InvalidOperationException(why);
            }
            var label = _request.Version == WindowsVersion.Windows11 ? "WIN11" : "WIN10";
            await WindowsTools.RunDiskpartAsync(WindowsMediaCommands.DiskpartScript(now, letter, label), line =>
            {
                if (line.Contains("percent", StringComparison.OrdinalIgnoreCase) || line.Contains("százalék", StringComparison.OrdinalIgnoreCase))
                {
                    StepChanged?.Invoke("format", StepState.Running, line.Trim());
                }
            }, ct);
            // A Windows néha egy pillanatig nem látja az új kötetet.
            for (var i = 0; i < 20 && !Directory.Exists(root); i++)
            {
                await Task.Delay(500, ct);
            }
            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException($"A formázás után a {root} meghajtó nem jelent meg.");
            }
            Log?.Invoke($"Formázva: {root} (FAT32)");
        });
        Overall?.Invoke(45);

        var wim = Path.Combine(isoRoot, "sources", "install.wim");
        var needsSplit = File.Exists(wim) && new FileInfo(wim).Length > InstallMediaValidator.Fat32MaxFileSize;
        await Step("copy", () => CopyTreeAsync(isoRoot, root, "copy", 45, 80, needsSplit ? wim : null, ct));

        await Step("split", async () =>
        {
            if (!needsSplit)
            {
                Skip("split", "nem kell (4 GB alatt)");
                return;
            }
            var target = Path.Combine(root, "sources", "install.swm");
            var progress = new Regex(@"(\d+(?:[.,]\d+)?)\s*%");
            var result = await WindowsTools.RunAsync("dism.exe", WindowsMediaCommands.DismSplitArgs(wim, target), line =>
            {
                var m = progress.Match(line);
                if (m.Success)
                {
                    StepChanged?.Invoke("split", StepState.Running, m.Groups[1].Value.Replace(',', '.') + "%");
                }
            }, ct);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("A DISM nem tudta feldarabolni az install.wim-et:" + Environment.NewLine + result.Output.Trim());
            }
        });
        Overall?.Invoke(90);

        await Step("boot", async () =>
        {
            var bootsect = Path.Combine(isoRoot, "boot", "bootsect.exe");
            if (!File.Exists(bootsect))
            {
                Skip("boot", "nincs bootsect.exe az ISO-ban – csak UEFI-s gépen bootol");
                return;
            }
            var result = await WindowsTools.RunAsync(bootsect, WindowsMediaCommands.BootsectArgs(letter), null, ct);
            if (result.ExitCode != 0)
            {
                // UEFI-s gépen enélkül is bootol - nem végzetes.
                Log?.Invoke("A bootsect nem sikerült (UEFI-s gépen ettől még bootol): " + result.Output.Trim());
            }
        });

        await Step("files", () => { WritePlan(plan, root, ct); return Task.CompletedTask; });
        await Step("verify", () => { Verify(root); return Task.CompletedTask; });
        Log?.Invoke($"Kész: {root}. Dugd a pendrive-ot az új gépbe, és indíts róla (a gép indításakor a boot-menü: általában F12, F11, F8 vagy Esc).");
    }

    private async Task BuildIsoAsync(string isoRoot, IReadOnlyList<PlannedFile> plan, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "WindowsTelepitoKeszito-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await Step("copy", () => CopyTreeAsync(isoRoot, work, "copy", 35, 75, null, ct));
            await Step("files", () => { WritePlan(plan, work, ct); return Task.CompletedTask; });
            await Step("iso", async () =>
            {
                var label = new DriveInfo(isoRoot).VolumeLabel;
                var args = WindowsMediaCommands.OscdimgArgs(work, _request.OutputIso!, label);
                var pct = new Regex(@"(\d+)%\s*complete", RegexOptions.IgnoreCase);
                var result = await WindowsTools.RunAsync(_request.OscdimgPath!, args, line =>
                {
                    var m = pct.Match(line);
                    if (m.Success)
                    {
                        StepChanged?.Invoke("iso", StepState.Running, m.Groups[1].Value + "%");
                    }
                }, ct);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException("Az oscdimg hibát jelzett:" + Environment.NewLine + result.Output.Trim());
                }
            });
            await Step("verify", () =>
            {
                var size = new FileInfo(_request.OutputIso!).Length;
                if (size < 100L * 1024 * 1024)
                {
                    throw new InvalidOperationException("A kész ISO gyanúsan kicsi.");
                }
                Log?.Invoke($"Kész: {_request.OutputIso} ({MediaWriter.FormatSize(size)})");
                return Task.CompletedTask;
            });
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }

    private void WritePlan(IReadOnlyList<PlannedFile> plan, string root, CancellationToken ct)
    {
        MediaWriter.Write(plan, root, new InlineProgress<string>(s => Log?.Invoke(s)), null, ct);
    }

    private void Verify(string root)
    {
        var problems = new List<string>();
        if (!File.Exists(Path.Combine(root, "setup.exe")))
        {
            problems.Add("hiányzik a setup.exe");
        }
        if (InstallMediaValidator.FindInstallImage(root) is null)
        {
            problems.Add("hiányzik a sources\\install.* képfájl");
        }
        if (!File.Exists(Path.Combine(root, "efi", "boot", "bootx64.efi")))
        {
            problems.Add("hiányzik az efi\\boot\\bootx64.efi (UEFI rendszerindítás)");
        }
        if (!File.Exists(Path.Combine(root, MediaLayout.AutounattendFile)))
        {
            problems.Add("hiányzik az autounattend.xml");
        }
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Az ellenőrzés hibát talált: " + string.Join(", ", problems));
        }
        Log?.Invoke("✓ Ellenőrzés rendben: setup.exe, telepítő képfájl, UEFI rendszerindítás, autounattend.xml");
    }

    /// <summary>Az ISO teljes tartalmának másolása folyamatjelzéssel (a <paramref name="skipFile"/>-t kihagyva).</summary>
    private async Task CopyTreeAsync(string sourceRoot, string targetRoot, string stepKey, int overallFrom, int overallTo, string? skipFile, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(f => skipFile is null || !string.Equals(f, skipFile, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var total = files.Sum(f => new FileInfo(f).Length);
        long done = 0;
        var buffer = new byte[4 << 20];
        var lastPct = -1;
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, files[i]);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var src = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true))
            await using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    var pct = total == 0 ? 100 : (int)(done * 100 / total);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        StepChanged?.Invoke(stepKey, StepState.Running, $"{pct}% · {i + 1} / {files.Count} fájl");
                        Overall?.Invoke(overallFrom + (overallTo - overallFrom) * pct / 100);
                    }
                }
            }
            // Az ISO-ról másolt fájlok írásvédettek - a munkamappában ez zavarna (pl. a törlésnél).
            File.SetAttributes(target, FileAttributes.Normal);
        }
        Log?.Invoke($"Átmásolva: {files.Count} fájl ({MediaWriter.FormatSize(total)})");
    }

    private readonly HashSet<string> _skipped = new();

    private void Skip(string key, string reason)
    {
        _skipped.Add(key);
        StepChanged?.Invoke(key, StepState.Skipped, reason);
    }

    private async Task Step(string key, Func<Task> action)
    {
        StepChanged?.Invoke(key, StepState.Running, null);
        try
        {
            await action();
            if (!_skipped.Contains(key))
            {
                StepChanged?.Invoke(key, StepState.Done, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StepChanged?.Invoke(key, StepState.Failed, ex.Message);
            throw;
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public InlineProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
