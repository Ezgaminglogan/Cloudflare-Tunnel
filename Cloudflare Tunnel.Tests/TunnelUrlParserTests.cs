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
}
