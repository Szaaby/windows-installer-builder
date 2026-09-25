global using Xunit;
global using InstallerBuilder.Core;

namespace InstallerBuilder.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wib-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}

public static class Profiles
{
    public static BuildProfile Sample(string? localInstaller = null)
    {
        var p = new BuildProfile();
        p.Programs.Add(new ProgramEntry { Name = "Google Chrome", Source = ProgramSource.Winget, WingetId = "Google.Chrome" });
        p.Programs.Add(new ProgramEntry { Name = "VLC", Source = ProgramSource.Winget, WingetId = "VideoLAN.VLC", DefaultSelected = false });
        if (localInstaller is not null)
        {
            p.Programs.Add(new ProgramEntry { Name = "Nyomtató driver", Source = ProgramSource.Local, LocalPath = localInstaller, SilentArgs = "/S" });
        }
        p.Windows.UserName = "Szabi";
        p.Windows.DisplayName = "Kovács Szabolcs";
        p.Windows.Password = "titkos123";
        p.Windows.ComputerName = "SZABI-PC";
        p.Installer.AutoStartSeconds = 60;
        return p;
    }
}
