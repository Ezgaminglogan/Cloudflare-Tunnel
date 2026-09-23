using System.IO.Compression;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class NgrokTunnelProvider : ITunnelProvider
{
    private readonly IBinaryManager _binaryManager;
    private string? _cachedBinaryPath;

    public TunnelProviderType Type => TunnelProviderType.Ngrok;
    public string DisplayName => "Ngrok Tunnel (Embedded Binary)";
    public string Description => "Embedded Ngrok engine. Requires free account authtoken from dashboard.ngrok.com.";
    public bool RequiresAuthToken => true;

    public NgrokTunnelProvider(IBinaryManager binaryManager)
    {
        _binaryManager = binaryManager;
    }

    public async Task<string> EnsureBinaryAvailableAsync(
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default)
    {
        if (_cachedBinaryPath != null && File.Exists(_cachedBinaryPath))
        {
            return _cachedBinaryPath;
        }

        // 1. Check if already extracted in %LOCALAPPDATA%\UniversalTunnel\bin
        string? existing = _binaryManager.FindExistingExecutable("ngrok");
        if (existing != null)
        {
            _cachedBinaryPath = existing;
            progress?.Report((1, 1, 0));
            return existing;
        }

        // 2. Extract from Embedded Resource
        try
        {
            string extracted = await _binaryManager.ExtractEmbeddedBinaryAsync(
                "ngrok.ngrok.exe",
                "ngrok.exe",
                progress,
                ct
            );
            _cachedBinaryPath = extracted;
            return extracted;
        }
        catch (FileNotFoundException)
        {
            // 3. Fallback: Download if embedded resource is missing
            var zipUrl = new Uri("https://bin.equinox.io/c/bNyj1mQVY4c/ngrok-v3-stable-windows-amd64.zip");
            string zipPath = await _binaryManager.DownloadBinaryAsync(zipUrl, "ngrok.zip", progress, ct);
            string targetExePath = Path.Combine(_binaryManager.GetBinaryDirectory(), "ngrok.exe");
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                var entry = archive.GetEntry("ngrok.exe") ?? archive.Entries.FirstOrDefault(e => e.Name.Equals("ngrok.exe", StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.ExtractToFile(targetExePath, overwrite: true);
                }
            }
            try { File.Delete(zipPath); } catch { }
            _cachedBinaryPath = targetExePath;
            return targetExePath;
        }
    }

    public async Task<ITunnelSession> StartTunnelAsync(TunnelOptions options, CancellationToken ct = default)
    {
        string binaryPath = await EnsureBinaryAvailableAsync(ct: ct);

        if (!string.IsNullOrWhiteSpace(options.AuthToken))
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"config add-authtoken {options.AuthToken}",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (proc != null) await proc.WaitForExitAsync(ct);
        }

        var session = new NgrokTunnelSession(binaryPath, options);
        session.Start();
        return session;
    }
}
