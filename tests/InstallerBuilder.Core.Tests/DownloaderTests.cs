using System.Net;
using System.Net.Http.Headers;

namespace InstallerBuilder.Core.Tests;

public class DownloaderTests
{
    private const string Page = "https://www.microsoft.com/en-us/software-download/windows11";

    [Fact]
    public void Parses_latest_edition_from_page()
    {
        var html = "<select><option value=\"\" selected>Select Download</option>" +
                   "<option value=\"3321\">Windows 11 (multi-edition ISO for x64 devices)</option>" +
                   "<option value=\"3322\">Windows 11 Home China</option></select>";
        var e = MicrosoftIsoDownloader.ParseLatestEdition(html, WindowsVersion.Windows11);
        Assert.Equal(("3321", "Windows 11 (multi-edition ISO for x64 devices)"), e!.Value);
        Assert.Null(MicrosoftIsoDownloader.ParseLatestEdition(html, WindowsVersion.Windows10));
    }

    [Fact]
    public void Picks_hungarian_sku()
    {
        var json = "{\"Skus\":[{\"Id\":\"20054\",\"Language\":\"English\",\"LocalizedLanguage\":\"English\"},{\"Id\":\"20055\",\"Language\":\"Hungarian\",\"LocalizedLanguage\":\"Hungarian\"}]}";
        Assert.Equal("20055", MicrosoftIsoDownloader.ParseSkuId(json, "Hungarian", Page));
        var ex = Assert.Throws<InvalidOperationException>(() => MicrosoftIsoDownloader.ParseSkuId(json, "Klingon", Page));
        Assert.Contains("English, Hungarian", ex.Message);
    }

    [Fact]
    public void Sentinel_rejection_becomes_blocked_exception()
    {
        // A valódi válasz, amit a Microsoft egy adatközpontos címre adott (2026-09-25).
        var json = "{\"Errors\":[{\"Key\":\"ErrorSettings.SentinelReject\",\"Value\":\"Sentinel marked this request as rejected.\",\"Type\":8}]}";
        var ex = Assert.Throws<MicrosoftDownloadBlockedException>(() => MicrosoftIsoDownloader.ParseDownloadLink(json, "x64", Page));
        Assert.Equal(Page, ex.PageUrl);
        Assert.Contains("Sentinel", ex.Message);
    }

    [Fact]
    public void Picks_x64_link()
    {
        var json = "{\"ProductDownloadOptions\":[" +
                   "{\"Uri\":\"https://software.download.prss.microsoft.com/dbazure/Win11_24H2_Hungarian_x64.iso?t=abc\",\"DownloadType\":1}]," +
                   "\"DownloadExpirationDatetime\":\"2026-09-26T09:00:00Z\"}";
        var link = MicrosoftIsoDownloader.ParseDownloadLink(json, "x64", Page);
        Assert.Equal("Win11_24H2_Hungarian_x64.iso", link.FileName);
        Assert.NotNull(link.Expires);
    }

    [Fact]
    public async Task Full_chain_with_fake_microsoft()
    {
        var handler = new FakeHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            Assert.Contains("Linux", req.Headers.UserAgent.ToString());
            if (url.StartsWith(Page)) return Text("<option value=\"3321\">Windows 11 (multi-edition ISO for x64 devices)</option>");
            if (url.Contains("vlscppe")) return Text("");
            if (url.Contains("getskuinformationbyproductedition"))
            {
                Assert.Contains("ProductEditionId=3321", url);
                return Text("{\"Skus\":[{\"Id\":\"20055\",\"Language\":\"Hungarian\",\"LocalizedLanguage\":\"Hungarian\"}]}");
            }
            if (url.Contains("GetProductDownloadLinksBySku"))
            {
                Assert.Contains("SKU=20055", url);
                Assert.Equal(Page, req.Headers.Referrer!.ToString());
                return Text("{\"ProductDownloadOptions\":[{\"Uri\":\"https://dl.example/Win11_Hungarian_x64.iso\"}]}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var d = new MicrosoftIsoDownloader(new HttpClient(handler));
        var link = await d.GetDownloadLinkAsync(WindowsVersion.Windows11, "Hungarian", "x64", CancellationToken.None);
        Assert.Equal("https://dl.example/Win11_Hungarian_x64.iso", link.Url);
    }

    [Fact]
    public async Task Download_resumes_from_partial_file()
    {
        var data = Enumerable.Range(0, 3_000_000).Select(i => (byte)(i % 251)).ToArray();
        string? rangeSeen = null;
        var handler = new FakeHandler(req =>
        {
            var from = req.Headers.Range?.Ranges.First().From ?? 0;
            rangeSeen = req.Headers.Range?.ToString();
            var r = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data.Skip((int)from).ToArray()),
            };
            return r;
        });
        using var tmp = new TempDir();
        var target = Path.Combine(tmp.Path, "win.iso");
        File.WriteAllBytes(target + ".partial", data.Take(1_000_000).ToArray());
        (long, long?) last = default;
        await new MicrosoftIsoDownloader(new HttpClient(handler)).DownloadFileAsync("https://dl.example/win.iso", target, new InlineProgress<(long, long?)>(p => last = p), CancellationToken.None);
        Assert.Equal("bytes=1000000-", rangeSeen);
        Assert.Equal(data, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + ".partial"));
        Assert.Equal((3_000_000L, (long?)3_000_000L), last);
    }

    private static HttpResponseMessage Text(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s) };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _f;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) => _f = f;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(_f(request));
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _a;
        public InlineProgress(Action<T> a) => _a = a;
        public void Report(T value) => _a(value);
    }
}
