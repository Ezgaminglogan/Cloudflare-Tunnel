using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class NgrokTunnelSession : ITunnelSession
{
    private readonly Process _process;
    private readonly TaskCompletionSource<bool> _exitTcs = new();
    private bool _disposed;
    private static readonly Regex UrlRegex = new(@"https://[a-zA-Z0-9-]+\.ngrok-free\.app", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TunnelProviderType Provider => TunnelProviderType.Ngrok;
    public int LocalPort { get; }
    public Uri? PublicUrl { get; private set; }
    public TunnelStatus Status { get; private set; } = TunnelStatus.Starting;
    public bool IsRunning => !_process.HasExited;

    public event Action<string>? OutputReceived;
    public event Action<Uri>? PublicUrlAssigned;
    public event Action<string>? StatusChanged;

    public NgrokTunnelSession(string ngrokPath, TunnelOptions options)
    {
        LocalPort = options.LocalPort;

        var startInfo = new ProcessStartInfo
        {
            FileName = ngrokPath,
            Arguments = $"http {options.LocalPort} --log=stdout",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ngrokPath) ?? AppContext.BaseDirectory
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) => ProcessOutput(e.Data);
        _process.ErrorDataReceived += (_, e) => ProcessOutput(e.Data);

        _process.Exited += (_, _) =>
        {
            Status = TunnelStatus.Stopped;
            StatusChanged?.Invoke("Ngrok process terminated.");
            _exitTcs.TrySetResult(true);
        };

        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        Console.CancelKeyPress += Console_CancelKeyPress;

        // Poll Ngrok local API in background in case stdout is suppressed
        _ = PollNgrokApiAsync();
    }

    public void Start()
    {
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private void ProcessOutput(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        OutputReceived?.Invoke(data);

        if (PublicUrl == null)
        {
            var match = UrlRegex.Match(data);
            if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
            {
                SetPublicUrl(uri);
            }
        }
    }

    private async Task PollNgrokApiAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (int i = 0; i < 20 && PublicUrl == null && !_process.HasExited; i++)
        {
            await Task.Delay(500);
            try
            {
                string json = await client.GetStringAsync("http://127.0.0.1:4040/api/tunnels");
                using var doc = JsonDocument.Parse(json);
                var tunnels = doc.RootElement.GetProperty("tunnels");
                foreach (var t in tunnels.EnumerateArray())
                {
                    if (t.TryGetProperty("public_url", out var urlProp))
                    {
                        string? url = urlProp.GetString();
                        if (!string.IsNullOrEmpty(url) && url.StartsWith("https://") && Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                        {
                            SetPublicUrl(parsed);
                            return;
                        }
                    }
                }
            }
            catch
            {
                // API not ready yet, continue polling
            }
        }
    }

    private void SetPublicUrl(Uri uri)
    {
        PublicUrl = uri;
        Status = TunnelStatus.Online;
        PublicUrlAssigned?.Invoke(uri);
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e) => KillProcessSafe();
    private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Intercept Ctrl+C so host app stays alive and returns to menu
        KillProcessSafe();
    }

    private void KillProcessSafe()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        using (cancellationToken.Register(() => _exitTcs.TrySetCanceled()))
        {
            return _exitTcs.Task;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_process.HasExited) return;

        try
        {
            _process.CancelOutputRead();
        }
        catch { }
        try
        {
            _process.CancelErrorRead();
        }
        catch { }

        try
        {
            _process.Kill(entireProcessTree: true);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            await _process.WaitForExitAsync(linked.Token);
        }
        catch { }

        Status = TunnelStatus.Stopped;
        StatusChanged?.Invoke("Tunnel stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
        Console.CancelKeyPress -= Console_CancelKeyPress;

        try
        {
            await StopAsync();
        }
        catch { }

        try
        {
            _process.Dispose();
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}
