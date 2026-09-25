using System.Diagnostics;
using System.Text;
using System.Text.Json;
using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>A Windows beépített eszközeinek hívása (PowerShell, diskpart, DISM, bootsect, oscdimg).</summary>
public static class WindowsTools
{
    public sealed record ProcessResult(int ExitCode, string Output);

    /// <summary>Folyamat futtatása; a kimenet soronként a <paramref name="onLine"/>-ra is megy (a folyamatjelzőhöz).</summary>
    public static async Task<ProcessResult> RunAsync(string file, string arguments, Action<string>? onLine = null, CancellationToken ct = default, string? stdin = null)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();
        void Handle(string? line)
        {
            if (line is null)
            {
                return;
            }
            lock (output)
            {
                output.AppendLine(line);
            }
            onLine?.Invoke(line);
        }
        p.OutputDataReceived += (_, e) => Handle(e.Data);
        p.ErrorDataReceived += (_, e) => Handle(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }
        using (ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { } }))
        {
            await p.WaitForExitAsync(ct);
        }
        return new ProcessResult(p.ExitCode, output.ToString());
    }

    /// <summary>PowerShell parancs futtatása (UTF-8 kimenettel). Hibánál kivételt dob a kimenettel.</summary>
    public static async Task<string> PowerShellAsync(string script, CancellationToken ct = default)
    {
        var full = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var r = await RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", null, ct);
        if (r.ExitCode != 0)
        {
            throw new InvalidOperationException("PowerShell hiba: " + r.Output.Trim());
        }
        return r.Output.Trim();
    }

    /// <summary>Szöveg PowerShell egyszeres idézőjelbe.</summary>
    public static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>A gép lemezei (Get-Disk), a meghajtóbetűikkel.</summary>
    public static async Task<List<DiskInfo>> GetDisksAsync(CancellationToken ct = default)
    {
        var json = await PowerShellAsync(
            "@(Get-Disk | ForEach-Object { $d = $_; [pscustomobject]@{ Number = $d.Number; FriendlyName = [string]$d.FriendlyName; " +
            "BusType = [string]$d.BusType; Size = [int64]$d.Size; IsSystem = [bool]$d.IsSystem; IsBoot = [bool]$d.IsBoot; " +
            "Letters = @(Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter } | ForEach-Object { [string]$_.DriveLetter }) } }) | ConvertTo-Json -Depth 3 -Compress",
            ct);
        var list = new List<DiskInfo>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return list;
        }
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        foreach (var d in items)
        {
            var letters = new List<string>();
            if (d.TryGetProperty("Letters", out var l))
            {
                if (l.ValueKind == JsonValueKind.Array)
                {
                    letters.AddRange(l.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
                }
                else if (l.ValueKind == JsonValueKind.String)
                {
                    letters.Add(l.GetString()!);
                }
            }
            list.Add(new DiskInfo(
                d.GetProperty("Number").GetInt32(),
                d.GetProperty("FriendlyName").GetString() ?? "",
                d.GetProperty("BusType").GetString() ?? "",
                d.GetProperty("Size").GetInt64(),
                d.GetProperty("IsSystem").GetBoolean(),
                d.GetProperty("IsBoot").GetBoolean(),
                letters));
        }
        return list;
    }

    /// <summary>ISO csatolása; visszaadja a gyökerét (pl. "F:\").</summary>
    public static async Task<string> MountIsoAsync(string isoPath, CancellationToken ct = default)
    {
        var letter = await PowerShellAsync(
            $"$img = Mount-DiskImage -ImagePath {Quote(isoPath)} -PassThru; $l = $null; " +
            "for ($i = 0; $i -lt 20 -and -not $l; $i++) { $l = ($img | Get-Volume).DriveLetter; if (-not $l) { Start-Sleep -Milliseconds 500 } }; " +
            "if (-not $l) { throw 'Az ISO csatolása után nem kapott meghajtóbetűt.' }; $l",
            ct);
        return letter.Trim().Substring(0, 1) + @":\";
    }

    public static async Task DismountIsoAsync(string isoPath)
    {
        try
        {
            await PowerShellAsync($"Dismount-DiskImage -ImagePath {Quote(isoPath)} | Out-Null");
        }
        catch
        {
            // A leválasztás hibája nem akaszthatja meg a készítést.
        }
    }

    public static async Task RunDiskpartAsync(string script, Action<string>? onLine, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"wtk-diskpart-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, script, Encoding.ASCII, ct);
        try
        {
            var r = await RunAsync("diskpart.exe", $"/s \"{file}\"", onLine, ct);
            if (r.ExitCode != 0)
            {
                throw new InvalidOperationException("A diskpart hibát jelzett:" + Environment.NewLine + r.Output.Trim());
            }
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    /// <summary>Egy szabad meghajtóbetű (Z-től visszafelé).</summary>
    public static char FreeDriveLetter()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var c = 'Z'; c >= 'G'; c--)
        {
            if (!used.Contains(c))
            {
                return c;
            }
        }
        throw new InvalidOperationException("Nincs szabad meghajtóbetű.");
    }

    public static string? FindOscdimg()
    {
        var candidates = WindowsMediaCommands.OscdimgCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // böngésző nélküli gépen nincs mit tenni
        }
    }

    public static string DownloadsFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WindowsTelepitoKeszito");
}
