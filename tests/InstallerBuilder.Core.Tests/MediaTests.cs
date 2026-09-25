using System.Text;
using System.Text.Json;

namespace InstallerBuilder.Core.Tests;

public class MediaTests
{
    [Fact]
    public void Plan_contains_expected_files()
    {
        using var tmp = new TempDir();
        var installer = tmp.File("src/Canon Setup.exe", "fake installer");
        var plan = MediaPlanner.Plan(Profiles.Sample(installer));
        var paths = plan.Select(f => f.RelativePath).ToList();
        var dir = "sources/$OEM$/$$/Setup/Scripts/ProgramTelepito";
        Assert.Contains("autounattend.xml", paths);
        Assert.Contains($"{dir}/Install-Programs.ps1", paths);
        Assert.Contains($"{dir}/Programok-telepitese.cmd", paths);
        Assert.Contains($"{dir}/programok.json", paths);
        Assert.Contains($"{dir}/Specialize.ps1", paths);
        Assert.Contains($"{dir}/felhasznalo.json", paths);
        Assert.Contains($"{dir}/telepitok/03-Canon Setup.exe", paths);

        var script = plan.Single(f => f.RelativePath.EndsWith("Install-Programs.ps1")).Content!;
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, script.Take(3));
        Assert.Contains("Show-ProgramSelection", Encoding.UTF8.GetString(script));

        var json = JsonDocument.Parse(plan.Single(f => f.RelativePath.EndsWith("programok.json")).Content!).RootElement;
        Assert.Equal(60, json.GetProperty("autoStartSeconds").GetInt32());
        var programs = json.GetProperty("programs").EnumerateArray().ToList();
        Assert.Equal("winget", programs[0].GetProperty("type").GetString());
        Assert.Equal("Google.Chrome", programs[0].GetProperty("id").GetString());
        Assert.False(programs[1].GetProperty("selected").GetBoolean());
        Assert.Equal("local", programs[2].GetProperty("type").GetString());
        Assert.Equal("telepitok/03-Canon Setup.exe", programs[2].GetProperty("file").GetString());
        Assert.Equal("/S", programs[2].GetProperty("args").GetString());
        Assert.Equal("Nyomtató driver", programs[2].GetProperty("name").GetString());

        var user = JsonDocument.Parse(plan.Single(f => f.RelativePath.EndsWith("felhasznalo.json")).Content!).RootElement;
        Assert.Equal("Szabi", user.GetProperty("name").GetString());
        Assert.Equal("titkos123", user.GetProperty("password").GetString());
    }

    [Fact]
    public void Validation_reports_problems()
    {
        var p = Profiles.Sample("/nincs/ilyen/fajl.exe");
        p.Windows.UserName = "";
        p.Windows.ComputerName = "tul-hosszu-szamitogepnev";
        p.Programs.Add(new ProgramEntry { Name = "Hibás", Source = ProgramSource.Winget, WingetId = "" });
        var problems = MediaPlanner.Validate(p);
        Assert.Contains(problems, x => x.Contains("felhasználó nevét"));
        Assert.Contains(problems, x => x.Contains("számítógép neve"));
        Assert.Contains(problems, x => x.Contains("nem található"));
        Assert.Contains(problems, x => x.Contains("winget azonosító"));
        Assert.Throws<ProfileValidationException>(() => MediaPlanner.Plan(p));
    }

    [Fact]
    public void Writer_backs_up_existing_unattend_and_replaces_old_folder()
    {
        using var media = new TempDir();
        media.File("autounattend.xml", "<regi/>");
        media.File("sources/$OEM$/$$/Setup/Scripts/ProgramTelepito/telepitok/regi.exe", "régi");
        using var src = new TempDir();
        var installer = src.File("driver.msi", new string('x', 5000));

        var logs = new List<string>();
        MediaWriter.Write(MediaPlanner.Plan(Profiles.Sample(installer)), media.Path, new SyncProgress<string>(logs.Add));

        Assert.Equal("<regi/>", File.ReadAllText(Path.Combine(media.Path, "autounattend.xml.bak")));
        Assert.Contains("urn:schemas-microsoft-com:unattend", File.ReadAllText(Path.Combine(media.Path, "autounattend.xml")));
        var dir = Path.Combine(media.Path, "sources", "$OEM$", "$$", "Setup", "Scripts", "ProgramTelepito");
        Assert.False(File.Exists(Path.Combine(dir, "telepitok", "regi.exe")));
        Assert.Equal(5000, new FileInfo(Path.Combine(dir, "telepitok", "03-driver.msi")).Length);
        Assert.Contains(logs, l => l.Contains("autounattend.xml.bak"));

        // Másodszor: a .bak már létezik - időbélyeges másolat készül, semmi nem vész el.
        MediaWriter.Write(MediaPlanner.Plan(Profiles.Sample(installer)), media.Path);
        Assert.True(Directory.GetFiles(media.Path, "autounattend.xml.*.bak").Length == 1);
    }

    [Fact]
    public void Validator_recognizes_install_media_and_space()
    {
        using var media = new TempDir();
        var plan = MediaPlanner.Plan(Profiles.Sample());
        var bad = InstallMediaValidator.Check(media.Path, plan, "NTFS", 10L << 30);
        Assert.Contains(bad, c => c.Level == CheckLevel.Error && c.Message.Contains("nem Windows telepítő"));

        media.File("setup.exe");
        media.File("Sources/Install.ESD");
        var good = InstallMediaValidator.Check(media.Path, plan, "NTFS", 10L << 30);
        Assert.True(InstallMediaValidator.HasNoErrors(good));
        Assert.Contains(good, c => c.Message.Contains("Install.ESD"));

        var full = InstallMediaValidator.Check(media.Path, plan, "NTFS", 1024);
        Assert.Contains(full, c => c.Level == CheckLevel.Error && c.Message.Contains("Nincs elég hely"));
    }

    [Fact]
    public void Fat32_rejects_files_over_4gb()
    {
        using var tmp = new TempDir();
        var big = Path.Combine(tmp.Path, "big.exe");
        using (var fs = File.Create(big))
        {
            fs.SetLength(4L * 1024 * 1024 * 1024 + 10); // ritka (sparse) fájl - nem foglal valódi helyet
        }
        var plan = new List<PlannedFile> { new("x/big.exe", null, big) };
        Assert.Contains(InstallMediaValidator.CheckFileSystem(plan, "FAT32"), c => c.Level == CheckLevel.Error);
        Assert.Empty(InstallMediaValidator.CheckFileSystem(plan, "NTFS"));
    }

    [Fact]
    public void Profile_roundtrip()
    {
        var p = Profiles.Sample();
        var back = ProfileStore.Deserialize(ProfileStore.Serialize(p));
        Assert.Equal(p.Programs.Count, back.Programs.Count);
        Assert.Equal("VideoLAN.VLC", back.Programs[1].WingetId);
        Assert.False(back.Programs[1].DefaultSelected);
        Assert.Equal("Szabi", back.Windows.UserName);
        Assert.Contains("\"source\": \"Winget\"", ProfileStore.Serialize(p));
        Assert.Contains("Kovács", ProfileStore.Serialize(p)); // ékezet olvashatóan, nem \u00e1
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}
