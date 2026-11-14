using System.Text.Json;
using Cloudflare_Tunnel.Infrastructure;
using Xunit;

namespace Cloudflare_Tunnel.Tests;

public class CloudflareUrlDetectorTests
{
    private readonly CloudflareUrlDetector _detector = new();

    [Fact]
    public void DetectsUrlOnSingleBoxLine()
    {
        string line = "2024-01-15T10:30:00Z INF |  https://lucky-otter-wild-sea.trycloudflare.com  |";

        bool found = _detector.TryDetect(line, out var url);

        Assert.True(found);
        Assert.Equal("https://lucky-otter-wild-sea.trycloudflare.com/", url!.ToString());
    }

    [Fact]
    public void DetectsUrlWrappedAcrossBoxLines()
    {
        // cloudflared wraps long hostnames inside its ASCII box
        Assert.False(_detector.TryDetect("INF |  https://very-long-quick-tun", out _));
        Assert.True(_detector.TryDetect("nel-hostname.trycloudflare.com  |", out var url));
        Assert.Equal("https://very-long-quick-tunnel-hostname.trycloudflare.com/", url!.ToString());
    }

    [Theory]
    [InlineData(
        "2024-01-15T10:30:00Z INF |  https://very-long-quick-tun  |",
        "2024-01-15T10:30:00Z INF |  nel-hostname.trycloudflare.com  |")]
    [InlineData(
        "INF |  https://very-long-quick-tun  |",
        "INF |  nel-hostname.trycloudflare.com  |")]
    public void DetectsWrappedUrlWhenContinuationLineHasLogPrefix(string firstLine, string secondLine)
    {
        Assert.False(_detector.TryDetect(firstLine, out _));
        Assert.True(_detector.TryDetect(secondLine, out var url));
        Assert.Equal("https://very-long-quick-tunnel-hostname.trycloudflare.com/", url!.ToString());
    }

    [Theory]
    [InlineData("INF +---------------------------------------------------+")]
    [InlineData("INF |  Your quick Tunnel has been created!             |")]
    [InlineData("INF Starting tunnel tunnelID=abc123")]
    public void IgnoresLinesWithoutUrl(string line)
    {
        Assert.False(_detector.TryDetect(line, out var url));
        Assert.Null(url);
    }

    [Fact]
    public void DoesNotMatchOtherDomains()
    {
        Assert.False(_detector.TryDetect("visit https://example.com for docs", out _));
        Assert.False(_detector.TryDetect("https://trycloudflare.com.evil.example", out _));
    }
}

public class NgrokUrlParserTests
{
    [Theory]
    [InlineData("t=2024 lvl=info msg=\"started tunnel\" url=https://abc123.ngrok-free.app", "https://abc123.ngrok-free.app/")]
    [InlineData("t=2024 lvl=info url=https://my-app.ngrok-free.dev", "https://my-app.ngrok-free.dev/")]
    [InlineData("forwarding https://x-y-z.ngrok.io -> http://localhost:5000", "https://x-y-z.ngrok.io/")]
    [InlineData("url=https://deep.sub.domain.ngrok.app", "https://deep.sub.domain.ngrok.app/")]
    public void DetectsNgrokUrlVariants(string line, string expected)
    {
        Assert.True(NgrokUrlParser.TryDetect(line, out var url));
        Assert.Equal(expected, url!.ToString());
    }

    [Theory]
    [InlineData("url=http://abc.ngrok-free.app")]       // http only — we surface https
    [InlineData("see https://ngrok.com pricing")]        // no subdomain
    [InlineData("random log line")]
    public void RejectsNonTunnelUrls(string line)
    {
        Assert.False(NgrokUrlParser.TryDetect(line, out _));
    }

    private static JsonDocument ParseTunnels(string json) => JsonDocument.Parse(json);

    [Fact]
    public void SelectsTunnelMatchingOurLocalPort()
    {
        const string json = """
            { "tunnels": [
                { "public_url": "https://other-user.ngrok-free.app", "proto": "https",
                  "config": { "addr": "http://localhost:9999" } },
                { "public_url": "https://ours.ngrok-free.app", "proto": "https",
                  "config": { "addr": "http://localhost:5000" } }
            ]}
            """;

        using var doc = ParseTunnels(json);
        Assert.True(NgrokUrlParser.TrySelectTunnel(doc.RootElement, 5000, out var url));
        Assert.Equal("https://ours.ngrok-free.app/", url!.ToString());
    }

    [Fact]
    public void SkipsTunnelsTargetingOtherPorts()
    {
        const string json = """
            { "tunnels": [
                { "public_url": "https://not-ours.ngrok-free.app", "proto": "https",
                  "config": { "addr": "http://localhost:9999" } }
            ]}
            """;

        using var doc = ParseTunnels(json);
        Assert.False(NgrokUrlParser.TrySelectTunnel(doc.RootElement, 5000, out _));
    }

    [Fact]
    public void DoesNotMatchPortPrefixCollisions()
    {
        const string json = """
            { "tunnels": [
                { "public_url": "https://port-50000.ngrok-free.app", "proto": "https",
                  "config": { "addr": "http://localhost:50000" } },
                { "public_url": "https://port-5000.ngrok-free.app", "proto": "https",
                  "config": { "addr": "http://localhost:5000" } }
            ]}
            """;

        using var doc = ParseTunnels(json);
        Assert.True(NgrokUrlParser.TrySelectTunnel(doc.RootElement, 5000, out var url));
        Assert.Equal("https://port-5000.ngrok-free.app/", url!.ToString());
    }

    [Fact]
    public void AcceptsTunnelWithoutAddrAsFallback()
    {
        const string json = """
            { "tunnels": [
                { "public_url": "https://only-one.ngrok-free.dev", "proto": "https" }
            ]}
            """;

        using var doc = ParseTunnels(json);
        Assert.True(NgrokUrlParser.TrySelectTunnel(doc.RootElement, 5000, out var url));
        Assert.Equal("https://only-one.ngrok-free.dev/", url!.ToString());
    }

    [Fact]
    public void IgnoresHttpOnlyTunnelUrls()
    {
        const string json = """
            { "tunnels": [
                { "public_url": "http://insecure.ngrok.io", "proto": "http" }
            ]}
            """;

        using var doc = ParseTunnels(json);
        Assert.False(NgrokUrlParser.TrySelectTunnel(doc.RootElement, 5000, out _));
    }
}

public class BinaryManagerTests
{
    [Theory]
    [InlineData("ngrok", "ngrok.exe")]
    [InlineData("cloudflared.exe", "cloudflared.exe")]
    [InlineData("ngrok.zip", "ngrok.zip")]         // regression: must not become ngrok.zip.exe
    [InlineData("archive.tar.gz", "archive.tar.gz")]
    public void AppendsExeOnlyWhenNoExtension(string input, string expected)
    {
        Assert.Equal(expected, BinaryManager.EnsureExecutableExtension(input));
    }
}

public class NgrokSessionArgumentTests
{
    [Fact]
    public void BuildsBasicHttpArguments()
    {
        string args = NgrokTunnelSession.BuildArguments(new Core.TunnelOptions(5000));
        Assert.Equal("http localhost:5000 --log=stdout", args);
    }

    [Fact]
    public void AppendsUrlForReservedDomain()
    {
        string args = NgrokTunnelSession.BuildArguments(
            new Core.TunnelOptions(5000, CustomSubdomain: "myapp.ngrok-free.dev"));
        Assert.Equal("http localhost:5000 --log=stdout --url=https://myapp.ngrok-free.dev", args);
    }

    [Fact]
    public void KeepsExplicitSchemeInDomain()
    {
        string args = NgrokTunnelSession.BuildArguments(
            new Core.TunnelOptions(5000, CustomSubdomain: "https://api.example.com/"));
        Assert.Equal("http localhost:5000 --log=stdout --url=https://api.example.com", args);
    }

    [Theory]
    [InlineData("myapp.ngrok-free.dev --log=evil")]
    [InlineData("myapp.ngrok-free.dev\t--config=foo")]
    public void RejectsCustomSubdomainWithEmbeddedWhitespace(string maliciousDomain)
    {
        Assert.Throws<ArgumentException>(() =>
            NgrokTunnelSession.BuildArguments(new Core.TunnelOptions(5000, CustomSubdomain: maliciousDomain)));
    }

    [Theory]
    [InlineData("# authtoken: some_commented_token", false)]
    [InlineData("version: \"2\"\nauthtoken: \"\"\n", false)]
    [InlineData("version: \"2\"\nauthtoken:   \n", false)]
    [InlineData("version: \"2\"\nauthtoken: 2abc123_validToken\n", true)]
    [InlineData("authtoken: '2abc123_validToken'", true)]
    public void DetectsOnlyNonEmptyUncommentedNgrokAuthToken(string yaml, bool expected)
    {
        Assert.Equal(expected, NgrokTunnelProvider.HasConfiguredAuthToken(yaml));
    }
}

public class SystemPortScannerTests
{
    [Fact]
    public void ParsesBothIpv4AndIpv6ListeningRows()
    {
        const string sampleNetstat = """
            Active Connections

              Proto  Local Address          Foreign Address        State           PID
              TCP    0.0.0.0:3000           0.0.0.0:0              LISTENING       1234
              TCP    [::1]:5173             [::]:0                 LISTENING       5678
              TCP    127.0.0.1:5000         127.0.0.1:54321        ESTABLISHED     9999
              UDP    0.0.0.0:5353           *:*                                    1111
            """;

        var owners = SystemPortScanner.ParseNetstatListeners(sampleNetstat);

        Assert.Equal(2, owners.Count);
        Assert.Equal((3000, "0.0.0.0", 1234), (owners[0].Port, owners[0].Address, owners[0].Pid));
        Assert.Equal((5173, "::1", 5678), (owners[1].Port, owners[1].Address, owners[1].Pid));
    }
}

public class AppUpdaterTests
{
    [Theory]
    [InlineData("2.0.0", "v2.1.0", true)]
    [InlineData("v2.0.0", "v2.0.1", true)]
    [InlineData("2.0.0", "v2.0.0", false)]
    [InlineData("2.1.0", "v2.0.0", false)]
    [InlineData("2.0.0", "not-a-version", false)]
    public void ComparesSemanticVersionsCorrectly(string currentVersion, string latestTag, bool expected)
    {
        Assert.Equal(expected, AppUpdater.IsNewerVersion(currentVersion, latestTag));
    }

    [Fact]
    public void ParsesGitHubLatestReleaseJsonWithExeAsset()
    {
        const string json = """
            {
              "tag_name": "v2.1.0",
              "name": "Cloudflare Tunnel v2.1.0",
              "body": "## What's Changed\r\n- Added auto-updater with MessageBox\r\n- Fixed IPv6 port scanner",
              "html_url": "https://github.com/Ezgaminglogan/Cloudflare-Tunnel/releases/tag/v2.1.0",
              "assets": [
                {
                  "name": "Cloudflare-Tunnel-v2.1.0-win-x64.zip",
                  "browser_download_url": "https://github.com/Ezgaminglogan/Cloudflare-Tunnel/releases/download/v2.1.0/Cloudflare-Tunnel-v2.1.0-win-x64.zip"
                },
                {
                  "name": "Cloudflare.Tunnel.exe",
                  "browser_download_url": "https://github.com/Ezgaminglogan/Cloudflare-Tunnel/releases/download/v2.1.0/Cloudflare.Tunnel.exe"
                }
              ]
            }
            """;

        using var doc = JsonDocument.Parse(json);
        Assert.True(AppUpdater.TryParseRelease(doc.RootElement, out var release));
        Assert.Equal("v2.1.0", release!.TagName);
        Assert.Equal("https://github.com/Ezgaminglogan/Cloudflare-Tunnel/releases/tag/v2.1.0", release.HtmlUrl);
        Assert.Equal("https://github.com/Ezgaminglogan/Cloudflare-Tunnel/releases/download/v2.1.0/Cloudflare.Tunnel.exe", release.ExeDownloadUrl);
        Assert.Contains("Added auto-updater with MessageBox", release.ReleaseNotes);
        Assert.Contains("Fixed IPv6 port scanner", release.ReleaseNotes);
    }

    [Fact]
    public void StagesUpdateInWritableLocalAppDataInsteadOfProtectedExeDir()
    {
        string stagingPath = AppUpdater.GetUpdateStagingPath("v2.1.0");
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, stagingPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".exe", stagingPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildElevatedSwapArgumentsQuotesPathsSafely()
    {
        string args = AppUpdater.BuildElevatedSwapCommand(
            @"C:\Cloudflare Tunnel.exe",
            @"C:\Cloudflare Tunnel.exe.old",
            @"C:\Users\Logan\AppData\Local\UniversalTunnel\updates\Cloudflare-Tunnel-v2.1.0.exe");

        Assert.Contains("\"C:\\Cloudflare Tunnel.exe\"", args);
        Assert.Contains("\"C:\\Cloudflare Tunnel.exe.old\"", args);
        Assert.Contains("move /y", args);
    }
}
