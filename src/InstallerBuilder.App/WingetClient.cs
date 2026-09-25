using InstallerBuilder.Core;

namespace InstallerBuilder.App;

/// <summary>A készítő gép winget-je: keresés és azonosító-ellenőrzés.</summary>
public static class WingetClient
{
    public static async Task<List<WingetSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var r = await WindowsTools.RunAsync("winget.exe",
            $"search --query \"{query.Replace("\"", "")}\" --source winget --accept-source-agreements", null, ct);
        return WingetOutputParser.ParseSearch(r.Output);
    }

    /// <summary>Létezik-e pontosan ez a csomag-azonosító a winget katalógusban.</summary>
    public static async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        var r = await WindowsTools.RunAsync("winget.exe",
            $"show --id \"{id.Replace("\"", "")}\" --exact --source winget --accept-source-agreements", null, ct);
        return r.ExitCode == 0;
    }

    public static bool IsAvailable()
    {
        try
        {
            var r = WindowsTools.RunAsync("winget.exe", "--version").GetAwaiter().GetResult();
            return r.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
