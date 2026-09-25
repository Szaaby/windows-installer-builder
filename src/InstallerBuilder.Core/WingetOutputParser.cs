namespace InstallerBuilder.Core;

public sealed record WingetSearchResult(string Name, string Id, string Version, string Source, bool Truncated);

/// <summary>
/// A "winget search" táblázatos kimenetének feldolgozása. A winget nem ad gépi (JSON) kimenetet a kereséshez, és a
/// fejléc nyelve a Windows nyelvét követi ("Name Id Version" / "Név Azonosító Verzió") - ezért az oszlopok HELYÉT a
/// fejléc-sor szavaiból vesszük (a fejlécet egy "-----" sor követi). A winget a szűk oszlopokat "…"-vel vágja le:
/// ilyenkor az azonosító nem használható, a találat Truncated jelzést kap.
/// </summary>
public static class WingetOutputParser
{
    public static List<WingetSearchResult> ParseSearch(string output)
    {
        var results = new List<WingetSearchResult>();
        // A winget a folyamatjelzőt \r-rel írja felül - csak az utolsó szakasz számít soronként.
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Contains('\r') ? l[(l.LastIndexOf('\r') + 1)..] : l)
            .ToList();
        var sep = lines.FindIndex(l => l.Trim().Length >= 10 && l.Trim().All(c => c == '-'));
        if (sep < 1)
        {
            return results;
        }
        var header = lines[sep - 1];
        var starts = ColumnStarts(header);
        if (starts.Count < 3)
        {
            return results;
        }
        foreach (var line in lines.Skip(sep + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            string Col(int i)
            {
                if (starts[i] >= line.Length)
                {
                    return "";
                }
                var end = i + 1 < starts.Count ? Math.Min(starts[i + 1], line.Length) : line.Length;
                return line[starts[i]..end].Trim();
            }
            var name = Col(0);
            var id = Col(1);
            if (id.Length == 0)
            {
                continue;
            }
            var version = Col(2);
            var source = starts.Count > 3 ? Col(starts.Count - 1) : "";
            var truncated = id.EndsWith('…') || id.Contains(' ');
            results.Add(new WingetSearchResult(name, id, version, source, truncated));
        }
        return results;
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                starts.Add(i);
            }
        }
        return starts;
    }
}
