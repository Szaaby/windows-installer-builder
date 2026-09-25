namespace InstallerBuilder.Core.Tests;

public class ParserTests
{
    [Fact]
    public void Winget_search_english_output()
    {
        var output = "   - \r   \\ \r   | \r" +
            "Name                         Id                                  Version     Match           Source\n" +
            "-----------------------------------------------------------------------------------------------------\n" +
            "Mozilla Firefox              Mozilla.Firefox                     131.0.2     Moniker: firefox winget\n" +
            "Mozilla Firefox (hu)         Mozilla.Firefox.hu                  131.0.2                     winget\n" +
            "Firefox Developer Edition    Mozilla.Firefox.DeveloperEdition    132.0b9                     winget\n" +
            "Some Very Long Name Package  Publisher.VeryLongPackageIdentifi…  1.0                         winget\n";
        var r = WingetOutputParser.ParseSearch(output);
        Assert.Equal(4, r.Count);
        Assert.Equal("Mozilla Firefox", r[0].Name);
        Assert.Equal("Mozilla.Firefox", r[0].Id);
        Assert.Equal("131.0.2", r[0].Version);
        Assert.Equal("winget", r[1].Source);
        Assert.Equal("Mozilla.Firefox.DeveloperEdition", r[2].Id);
        Assert.False(r[2].Truncated);
        Assert.True(r[3].Truncated);
    }

    [Fact]
    public void Winget_search_hungarian_output()
    {
        var output =
            "Név            Azonosító      Verzió   Egyezés        Forrás\n" +
            "-----------------------------------------------------------\n" +
            "7-Zip          7zip.7zip      24.08    Moniker: 7zip  winget\n";
        var r = WingetOutputParser.ParseSearch(output);
        Assert.Single(r);
        Assert.Equal("7zip.7zip", r[0].Id);
        Assert.Equal("24.08", r[0].Version);
    }

    [Fact]
    public void Winget_no_results()
    {
        Assert.Empty(WingetOutputParser.ParseSearch("No package found matching input criteria."));
    }

    [Fact]
    public void Installer_type_detection()
    {
        using var tmp = new TempDir();
        Assert.Equal("MSI", InstallerTypeDetector.Suggest(tmp.File("a.msi")).InstallerType);
        Assert.Equal("REG", InstallerTypeDetector.Suggest(tmp.File("a.reg")).InstallerType);
        var inno = InstallerTypeDetector.Suggest(tmp.File("inno.exe", "MZ....Inno Setup Setup Data (6.2.0)...."));
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-", inno.Args);
        Assert.Equal("/S", InstallerTypeDetector.Suggest(tmp.File("nsis.exe", "MZ..Nullsoft Install System v3.08")).Args);
        Assert.Equal("Ismeretlen", InstallerTypeDetector.Suggest(tmp.File("x.exe", "MZ semmi")).InstallerType);
    }

    [Fact]
    public void Curated_ids_are_unique_and_wellformed()
    {
        var ids = CuratedPrograms.All.Select(p => p.WingetId).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Matches(@"^[A-Za-z0-9][A-Za-z0-9.+\-_]*\.[A-Za-z0-9.+\-_]+$", id));
    }
}
