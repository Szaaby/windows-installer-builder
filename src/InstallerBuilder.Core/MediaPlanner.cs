using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace InstallerBuilder.Core;

/// <summary>Egy fájl, ami az adathordozóra kerül: vagy kész tartalom, vagy egy másolandó forrásfájl a készítő gépen.</summary>
public sealed record PlannedFile(string RelativePath, byte[]? Content, string? SourcePath)
{
    public long Length => Content?.LongLength ?? (SourcePath is not null && File.Exists(SourcePath) ? new FileInfo(SourcePath).Length : 0);
}

/// <summary>Hiba a profilban, ami miatt nem készíthető el a telepítő.</summary>
public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(IReadOnlyList<string> problems) : base(string.Join(Environment.NewLine, problems)) => Problems = problems;

    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Összeállítja, mi kerüljön az adathordozóra (a gyökérbe az autounattend.xml, a sources\$OEM$\... mappába a
/// programválasztó, a programlista, a saját telepítők). Ugyanezt a tervet írja ki a pendrive, a mappa és az ISO is.
/// </summary>
public static class MediaPlanner
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // A PowerShell 5.1 (a friss Windowson ez van) a BOM nélküli .ps1-et nem UTF-8-ként olvassa - az ékezetek miatt kell.
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    public static IReadOnlyList<string> Validate(BuildProfile profile)
    {
        var problems = new List<string>();
        var w = profile.Windows;
        if (w.CreateLocalUser)
        {
            if (string.IsNullOrWhiteSpace(w.UserName))
            {
                problems.Add("Add meg a helyi felhasználó nevét (Windows beállítások).");
            }
            else if (w.UserName.Trim().Length > 20 || w.UserName.IndexOfAny("\"/\\[]:;|=,+*?<>@".ToCharArray()) >= 0)
            {
                problems.Add("A felhasználónév legfeljebb 20 karakter lehet, és nem tartalmazhat \" / \\ [ ] : ; | = , + * ? < > @ jelet.");
            }
        }
        if (!string.IsNullOrWhiteSpace(w.ComputerName))
        {
            var name = w.ComputerName.Trim();
            if (name.Length > 15 || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9-]+$") || name.All(char.IsDigit))
            {
                problems.Add("A számítógép neve legfeljebb 15 karakter: betűk, számok és kötőjel (nem lehet csak szám).");
            }
        }
        for (var i = 0; i < profile.Programs.Count; i++)
        {
            var p = profile.Programs[i];
            var label = string.IsNullOrWhiteSpace(p.Name) ? $"{i + 1}. program" : p.Name;
            if (string.IsNullOrWhiteSpace(p.Name))
            {
                problems.Add($"{label}: hiányzik a neve.");
            }
            if (p.Source == ProgramSource.Winget && string.IsNullOrWhiteSpace(p.WingetId))
            {
                problems.Add($"{label}: hiányzik a winget azonosító.");
            }
            if (p.Source == ProgramSource.Local && (string.IsNullOrWhiteSpace(p.LocalPath) || !File.Exists(p.LocalPath)))
            {
                problems.Add($"{label}: a telepítő fájl nem található ({p.LocalPath}).");
            }
        }
        if (profile.Installer.AutoStartSeconds < 0 || profile.Installer.AutoStartSeconds > 3600)
        {
            problems.Add("A programválasztó visszaszámlálása 0 és 3600 mp között lehet.");
        }
        return problems;
    }

    public static IReadOnlyList<PlannedFile> Plan(BuildProfile profile)
    {
        var problems = Validate(profile);
        if (problems.Count > 0)
        {
            throw new ProfileValidationException(problems);
        }

        var files = new List<PlannedFile>
        {
            new(MediaLayout.AutounattendFile, Encoding.UTF8.GetBytes(AutounattendGenerator.Generate(profile)), null),
        };
        var dir = MediaLayout.MediaFolder;
        var hasPrograms = profile.Programs.Count > 0;
        var createUser = profile.Windows.CreateLocalUser && !string.IsNullOrWhiteSpace(profile.Windows.UserName);

        if (hasPrograms || createUser)
        {
            // A tartalék-másolás (lásd AutounattendGenerator.FallbackCopyCommand) erre a fájlra keres - mindig legyen ott.
            files.Add(new($"{dir}/{MediaLayout.InstallScript}", WithBom(EmbeddedScripts.Read(MediaLayout.InstallScript)), null));
        }
        if (hasPrograms)
        {
            files.Add(new($"{dir}/{MediaLayout.RerunCmd}", Encoding.ASCII.GetBytes(EmbeddedScripts.Read(MediaLayout.RerunCmd)), null));

            var entries = new List<object>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < profile.Programs.Count; i++)
            {
                var p = profile.Programs[i];
                if (p.Source == ProgramSource.Winget)
                {
                    entries.Add(new { name = p.Name.Trim(), type = "winget", id = p.WingetId!.Trim(), selected = p.DefaultSelected });
                    continue;
                }
                var fileName = UniqueFileName($"{i + 1:00}-{SafeFileName(Path.GetFileName(p.LocalPath!))}", usedNames);
                var relative = $"{MediaLayout.InstallerSubfolder}/{fileName}";
                files.Add(new($"{dir}/{relative}", null, p.LocalPath));
                entries.Add(new { name = p.Name.Trim(), type = "local", file = relative, args = p.SilentArgs?.Trim() ?? "", selected = p.DefaultSelected });
            }
            var config = new
            {
                autoStartSeconds = profile.Installer.AutoStartSeconds,
                rebootWhenDone = profile.Installer.RebootWhenDone,
                programs = entries,
            };
            files.Add(new($"{dir}/{MediaLayout.ProgramsJson}", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, Json)), null));
        }
        if (createUser)
        {
            files.Add(new($"{dir}/{MediaLayout.SpecializeScript}", WithBom(EmbeddedScripts.Read(MediaLayout.SpecializeScript)), null));
            var user = new
            {
                name = profile.Windows.UserName.Trim(),
                fullName = string.IsNullOrWhiteSpace(profile.Windows.DisplayName) ? null : profile.Windows.DisplayName!.Trim(),
                password = profile.Windows.Password ?? "",
            };
            files.Add(new($"{dir}/{MediaLayout.UserJson}", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(user, Json)), null));
        }
        return files;
    }

    public static long TotalBytes(IEnumerable<PlannedFile> files) => files.Sum(f => f.Length);

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' }).ToHashSet();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        }
        var result = sb.ToString().Trim().Trim('.');
        return result.Length == 0 ? "telepito" : result;
    }

    private static string UniqueFileName(string name, HashSet<string> used)
    {
        var candidate = name;
        var n = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)}-{n++}{Path.GetExtension(name)}";
        }
        return candidate;
    }

    private static byte[] WithBom(string text) => Utf8Bom.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
}
