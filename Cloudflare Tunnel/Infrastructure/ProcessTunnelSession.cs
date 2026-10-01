using System.Diagnostics;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

/// <summary>
/// Shared process-lifecycle plumbing for tunnel sessions: output redirection,
/// exit/cancel handling, public-URL assignment, and disposal. Subclasses supply
/// the command line and per-engine URL detection.
/// </summary>
public abstract class ProcessTunnelSession : ITunnelSession
{
    private readonly Process _process;
    private readonly TaskCompletionSource<bool> _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _urlLock = new();
    private volatile bool _started;
    private bool _disposed;

    public abstract TunnelProviderType Provider { get; }
    public int LocalPort { get; }
    public Uri? PublicUrl { get; private set; }
    public TunnelStatus Status { get; private set; } = TunnelStatus.Starting;
    public bool IsRunning => ProcessIsRunning();

    public event Action<string>? OutputReceived;
    public event Action<Uri>? PublicUrlAssigned;
    public event Action<TunnelStatus>? StatusChanged;

    protected ProcessTunnelSession(ProcessStartInfo startInfo, TunnelOptions options)
    {
        LocalPort = options.LocalPort;

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) => ProcessOutput(e.Data);
        _process.ErrorDataReceived += (_, e) => ProcessOutput(e.Data);

        _process.Exited += (_, _) =>
        {
            Status = _process.ExitCode == 0 ? TunnelStatus.Stopped : TunnelStatus.Faulted;
            StatusChanged?.Invoke(Status);
            _exitTcs.TrySetResult(true);
        };

        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        Console.CancelKeyPress += Console_CancelKeyPress;
    }

    /// <summary>Called with each stdout/stderr line; parse the public URL here.</summary>
    protected abstract void OnOutputLine(string line);

    /// <summary>Called once after the process has been started (e.g. to kick off API polling).</summary>
    protected virtual void OnStarted() { }

    public void Start()
    {
        if (_started) return;
        _process.Start();
        _started = true;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        OnStarted();
    }

    private void ProcessOutput(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        OutputReceived?.Invoke(data);
        OnOutputLine(data);
    }

    protected void SetPublicUrl(Uri uri)
    {
        lock (_urlLock)
        {
            if (PublicUrl != null) return;
            PublicUrl = uri;
            Status = TunnelStatus.Online;
            PublicUrlAssigned?.Invoke(uri);
        }
    }

    protected bool ProcessIsRunning()
    {
        try
        {
            return _started && !_process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    protected bool IsDisposed => _disposed;

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
            if (ProcessIsRunning())
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => _exitTcs.Task.WaitAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!ProcessIsRunning()) return;

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
        StatusChanged?.Invoke(Status);
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
