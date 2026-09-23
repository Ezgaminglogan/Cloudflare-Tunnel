using System.Diagnostics;
using System.Text.RegularExpressions;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public partial class CloudflareTunnelSession : ITunnelSession
{
    private readonly Process _process;
    private readonly TaskCompletionSource<bool> _exitTcs = new();
    private bool _disposed;
    private static readonly Regex UrlRegex = new(@"https://[a-zA-Z0-9-]+\.trycloudflare\.com", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TunnelProviderType Provider => TunnelProviderType.Cloudflare;
    public int LocalPort { get; }
    public Uri? PublicUrl { get; private set; }
    public TunnelStatus Status { get; private set; } = TunnelStatus.Starting;
    public bool IsRunning => !_process.HasExited;

    public event Action<string>? OutputReceived;
    public event Action<Uri>? PublicUrlAssigned;
    public event Action<string>? StatusChanged;

    public CloudflareTunnelSession(string cloudflaredPath, TunnelOptions options)
    {
        LocalPort = options.LocalPort;

        var startInfo = new ProcessStartInfo
        {
            FileName = cloudflaredPath,
            Arguments = $"tunnel --url http://{options.Hostname}:{options.LocalPort}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(cloudflaredPath) ?? AppContext.BaseDirectory
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) => ProcessOutput(e.Data);
        _process.ErrorDataReceived += (_, e) => ProcessOutput(e.Data);

        _process.Exited += (_, _) =>
        {
            Status = TunnelStatus.Stopped;
            StatusChanged?.Invoke("Cloudflare tunnel process terminated.");
            _exitTcs.TrySetResult(true);
        };

        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        Console.CancelKeyPress += Console_CancelKeyPress;
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
                PublicUrl = uri;
                Status = TunnelStatus.Online;
                PublicUrlAssigned?.Invoke(uri);
            }
        }
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        KillProcessSafe();
    }

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
