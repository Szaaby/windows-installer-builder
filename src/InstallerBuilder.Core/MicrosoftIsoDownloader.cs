using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InstallerBuilder.Core;

/// <summary>Letölthető Windows verzió.</summary>
public enum WindowsVersion
{
    Windows11,
    Windows10,
}

/// <summary>A Microsoft által elutasított automatikus letöltés - ilyenkor a letöltőoldalt kell kézzel megnyitni.</summary>
public sealed class MicrosoftDownloadBlockedException : Exception
{
    public MicrosoftDownloadBlockedException(string message, string pageUrl) : base(message) => PageUrl = pageUrl;

    /// <summary>A Microsoft letöltőoldala, ahonnan kézzel letölthető az ISO.</summary>
    public string PageUrl { get; }
}

public sealed record WindowsDownloadLink(string Url, string FileName, DateTimeOffset? Expires);

/// <summary>
/// A legfrissebb Windows ISO letöltése a Microsoft HIVATALOS szerveréről - ugyanazzal a kérés-sorral, amit a Microsoft
/// letöltőoldala (és a Rufus letöltője, a Fido) is használ:
///   1. a letöltőoldalról a legújabb kiadás azonosítója (&lt;option value="3321"&gt;Windows 11 ...);
///   2. munkamenet regisztrálása (vlscppe.microsoft.com - a Microsoft visszaélés-szűrője);
///   3. a kiadás nyelvei (SKU-k) - ebből a kért nyelv;
///   4. a nyelvhez tartozó, időkorlátos (24 órás) letöltési link az architektúrához.
/// A Microsoft ezt néha elutasítja ("Sentinel marked this request as rejected"), főleg adatközpontos címekről vagy
/// sok próbálkozás után: ilyenkor <see cref="MicrosoftDownloadBlockedException"/>, és a felület a letöltőoldalt nyitja meg.
/// </summary>
public sealed class MicrosoftIsoDownloader
{
    // A Microsoft letöltőoldala Windows-os böngészőnek a Media Creation Tool-t kínálja ISO helyett (a Windows 10-nél
    // mindig) - ezért nem-Windowsos böngészőként kérdezünk, ahogy a Fido is.
    public const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0";
    private const string Profile = "606624d44113";
    private const string OrgId = "y6jn8c31";

    private readonly HttpClient _http;

    public MicrosoftIsoDownloader(HttpClient http)
    {
        _http = http;
    }

    public static string PageUrl(WindowsVersion version) => version == WindowsVersion.Windows11
        ? "https://www.microsoft.com/en-us/software-download/windows11"
        : "https://www.microsoft.com/en-us/software-download/windows10ISO";

    /// <summary>A letöltőoldalon kínált legelső (= legújabb) többkiadásos ISO azonosítója és neve.</summary>
    public async Task<(string EditionId, string Name)> GetLatestEditionAsync(WindowsVersion version, CancellationToken ct)
    {
        var html = await GetStringAsync(PageUrl(version), null, ct);
        var edition = ParseLatestEdition(html, version);
        return edition ?? throw new MicrosoftDownloadBlockedException(
            "A Microsoft letöltőoldalán nem található letölthető ISO kiadás (a Microsoft megváltoztathatta az oldalt).", PageUrl(version));
    }

    public static (string EditionId, string Name)? ParseLatestEdition(string html, WindowsVersion version)
    {
        var prefix = version == WindowsVersion.Windows11 ? "Windows 11" : "Windows 10";
        foreach (Match m in Regex.Matches(html, @"<option\s+value=""(\d+)""[^>]*>([^<]+)</option>", RegexOptions.IgnoreCase))
        {
            var name = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (m.Groups[1].Value, name);
            }
        }
        return null;
    }

    /// <summary>A teljes lánc: a legújabb kiadás letöltési linkje a kért nyelvre és architektúrára.</summary>
    public async Task<WindowsDownloadLink> GetDownloadLinkAsync(WindowsVersion version, string language, string architecture, CancellationToken ct)
    {
        var (editionId, _) = await GetLatestEditionAsync(version, ct);
        var sessionId = Guid.NewGuid().ToString();
        var referer = PageUrl(version);

        // A visszaélés-szűrő munkamenete. Ha ez nem megy át (pl. tűzfal), a következő lépés úgyis elutasítással jelez.
        try
        {
            await GetStringAsync($"https://vlscppe.microsoft.com/tags?org_id={OrgId}&session_id={sessionId}", referer, ct);
        }
        catch (HttpRequestException)
        {
        }

        var skusJson = await GetStringAsync(
            $"https://www.microsoft.com/software-download-connector/api/getskuinformationbyproductedition?profile={Profile}&ProductEditionId={editionId}&SKU=undefined&friendlyFileName=undefined&Locale=en-US&sessionID={sessionId}",
            referer, ct);
        var skuId = ParseSkuId(skusJson, language, referer);

        var linksJson = await GetStringAsync(
            $"https://www.microsoft.com/software-download-connector/api/GetProductDownloadLinksBySku?profile={Profile}&ProductEditionId=undefined&SKU={skuId}&friendlyFileName=undefined&Locale=en-US&sessionID={sessionId}",
            referer, ct);
        return ParseDownloadLink(linksJson, architecture, referer);
    }

    public static string ParseSkuId(string json, string language, string pageUrl)
    {
        using var doc = JsonDocument.Parse(json);
        ThrowIfErrors(doc.RootElement, pageUrl);
        if (!doc.RootElement.TryGetProperty("Skus", out var skus) || skus.ValueKind != JsonValueKind.Array)
        {
            throw new MicrosoftDownloadBlockedException("A Microsoft nem adott nyelv-listát.", pageUrl);
        }
        var available = new List<string>();
        foreach (var sku in skus.EnumerateArray())
        {
            var lang = sku.TryGetProperty("Language", out var l) ? l.GetString() ?? "" : "";
            var localized = sku.TryGetProperty("LocalizedLanguage", out var ll) ? ll.GetString() ?? "" : "";
            available.Add(lang);
            if (lang.Equals(language, StringComparison.OrdinalIgnoreCase) || localized.Equals(language, StringComparison.OrdinalIgnoreCase))
            {
                return sku.GetProperty("Id").GetString() ?? throw new InvalidDataException("SKU azonosító hiányzik.");
            }
        }
        throw new InvalidOperationException($"A(z) \"{language}\" nyelv nem érhető el. Elérhető: {string.Join(", ", available)}");
    }

    public static WindowsDownloadLink ParseDownloadLink(string json, string architecture, string pageUrl)
    {
        using var doc = JsonDocument.Parse(json);
        ThrowIfErrors(doc.RootElement, pageUrl);
        if (!doc.RootElement.TryGetProperty("ProductDownloadOptions", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            throw new MicrosoftDownloadBlockedException("A Microsoft nem adott letöltési linket.", pageUrl);
        }
        DateTimeOffset? expires = null;
        if (doc.RootElement.TryGetProperty("DownloadExpirationDatetime", out var exp) && exp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(exp.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var e))
        {
            expires = e;
        }
        var wanted = architecture.ToLowerInvariant();
        foreach (var o in options.EnumerateArray())
        {
            var uri = o.TryGetProperty("Uri", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(uri))
            {
                continue;
            }
            var fileName = Path.GetFileName(new Uri(uri).AbsolutePath);
            var name = fileName.ToLowerInvariant();
            // A fájlnév jelzi az architektúrát (Win11_24H2_Hungarian_x64.iso, ..._Arm64.iso); egy-linkes válasznál az az egy.
            if (name.Contains(wanted) || (wanted == "x64" && name.Contains("x64")) || options.GetArrayLength() == 1)
            {
                return new WindowsDownloadLink(uri, fileName, expires);
            }
        }
        throw new InvalidOperationException($"Nincs letöltési link a(z) {architecture} architektúrához.");
    }

    private static void ThrowIfErrors(JsonElement root, string pageUrl)
    {
        if (root.TryGetProperty("Errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            var first = errors[0];
            var value = first.TryGetProperty("Value", out var v) ? v.GetString() : null;
            throw new MicrosoftDownloadBlockedException(
                "A Microsoft most nem engedi az automatikus letöltést" + (value is null ? "." : $" ({value})."), pageUrl);
        }
    }

    private async Task<string> GetStringAsync(string url, string? referer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        if (referer is not null)
        {
            request.Headers.Referrer = new Uri(referer);
        }
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>A letöltendő fájl mérete (HEAD kérés) - egy korábban már teljesen letöltött ISO felismeréséhez.</summary>
    public async Task<long?> GetRemoteLengthAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Nagy fájl letöltése folytatással: ha a célfájl részben már megvan (egy megszakadt letöltésből, ".partial"),
    /// onnan folytatja. A kész fájl csak a teljes letöltés után kapja meg a végleges nevét.
    /// </summary>
    public async Task DownloadFileAsync(string url, string targetPath, IProgress<(long Done, long? Total)>? progress, CancellationToken ct)
    {
        var partial = targetPath + ".partial";
        long existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (existing > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            // A szerver nem folytat - elölről.
            existing = 0;
        }
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength is { } len ? len + existing : null;

        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = new FileStream(partial, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            var buffer = new byte[1 << 20];
            long done = existing;
            var lastReport = DateTime.MinValue;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                {
                    progress?.Report((done, total));
                    lastReport = DateTime.UtcNow;
                }
            }
            progress?.Report((done, total));
        }
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
        File.Move(partial, targetPath);
    }
}
