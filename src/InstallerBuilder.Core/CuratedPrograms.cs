namespace InstallerBuilder.Core;

public sealed record CuratedProgram(string Name, string WingetId, string Category);

/// <summary>A "Népszerű programok" gyors-hozzáadó lista. Az azonosítókat a felület "Azonosítók ellenőrzése" gombja a winget-tel ellenőrzi.</summary>
public static class CuratedPrograms
{
    public static readonly IReadOnlyList<CuratedProgram> All = new List<CuratedProgram>
    {
        new("Google Chrome", "Google.Chrome", "Böngésző"),
        new("Mozilla Firefox", "Mozilla.Firefox", "Böngésző"),
        new("Brave", "Brave.Brave", "Böngésző"),
        new("7-Zip", "7zip.7zip", "Eszköz"),
        new("WinRAR", "RARLab.WinRAR", "Eszköz"),
        new("Notepad++", "Notepad++.Notepad++", "Eszköz"),
        new("Everything (fájlkereső)", "voidtools.Everything", "Eszköz"),
        new("PowerToys", "Microsoft.PowerToys", "Eszköz"),
        new("ShareX", "ShareX.ShareX", "Eszköz"),
        new("VLC media player", "VideoLAN.VLC", "Média"),
        new("K-Lite Codec Pack", "CodecGuide.K-LiteCodecPack.Standard", "Média"),
        new("OBS Studio", "OBSProject.OBSStudio", "Média"),
        new("Audacity", "Audacity.Audacity", "Média"),
        new("paint.net", "dotPDN.PaintDotNet", "Média"),
        new("Discord", "Discord.Discord", "Kommunikáció"),
        new("Telegram", "Telegram.TelegramDesktop", "Kommunikáció"),
        new("Viber", "Rakuten.Viber", "Kommunikáció"),
        new("Zoom", "Zoom.Zoom", "Kommunikáció"),
        new("Microsoft Teams", "Microsoft.Teams", "Kommunikáció"),
        new("Steam", "Valve.Steam", "Játék"),
        new("Epic Games Launcher", "EpicGames.EpicGamesLauncher", "Játék"),
        new("TeamViewer", "TeamViewer.TeamViewer", "Távelérés"),
        new("AnyDesk", "AnyDesk.AnyDesk", "Távelérés"),
        new("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit", "Iroda"),
        new("LibreOffice", "TheDocumentFoundation.LibreOffice", "Iroda"),
        new("Visual Studio Code", "Microsoft.VisualStudioCode", "Fejlesztés"),
        new("Git", "Git.Git", "Fejlesztés"),
        new("Python 3.12", "Python.Python.3.12", "Fejlesztés"),
        new("Ollama", "Ollama.Ollama", "AI"),
        new("Claude", "Anthropic.Claude", "AI"),
        new("KeePassXC", "KeePassXCTeam.KeePassXC", "Biztonság"),
        new("Bitwarden", "Bitwarden.Bitwarden", "Biztonság"),
        new("HWiNFO", "REALiX.HWiNFO", "Rendszer"),
        new("CPU-Z", "CPUID.CPU-Z", "Rendszer"),
        new("Visual C++ újraterjeszthető (x64)", "Microsoft.VCRedist.2015+.x64", "Rendszer"),
        new(".NET Desktop Runtime 8", "Microsoft.DotNet.DesktopRuntime.8", "Rendszer"),
    };
}
