using System.Diagnostics;
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

        string? existing = _binaryManager.FindExistingExecutable("ngrok");
        if (existing != null)
        {
            // Trust user-managed installs (PATH / app dir); refresh our own cache
            // copy when it no longer matches the embedded engine.
            bool isManagedByUs = existing.StartsWith(_binaryManager.GetBinaryDirectory(), StringComparison.OrdinalIgnoreCase);
            long? embeddedLength = BinaryManager.GetEmbeddedResourceLength("ngrok.ngrok.exe");
            if (!isManagedByUs || embeddedLength == null || new FileInfo(existing).Length == embeddedLength)
            {
                _cachedBinaryPath = existing;
                progress?.Report((1, 1, 0));
                return existing;
            }
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
            try
            {
                string targetExePath = Path.Combine(_binaryManager.GetBinaryDirectory(), "ngrok.exe");
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.GetEntry("ngrok.exe")
                        ?? archive.Entries.FirstOrDefault(e => e.Name.Equals("ngrok.exe", StringComparison.OrdinalIgnoreCase));
                    entry?.ExtractToFile(targetExePath, overwrite: true);
                }

                if (!File.Exists(targetExePath))
                {
                    throw new FileNotFoundException("ngrok.exe was not found inside the downloaded archive.");
                }

                _cachedBinaryPath = targetExePath;
                return targetExePath;
            }
            finally
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Locates an existing ngrok config file containing an authtoken, so the
    /// user isn't re-prompted every run. Covers both the v3 OS config dir and
    /// the legacy v2 profile location.
    /// </summary>
    public static string? FindSavedConfigPath()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ngrok", "ngrok.yml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ngrok2", "ngrok.yml")
        };

        foreach (string path in candidates)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Contains("authtoken", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch { }
        }

        return null;
    }

    public async Task<ITunnelSession> StartTunnelAsync(TunnelOptions options, CancellationToken ct = default)
    {
        string binaryPath = await EnsureBinaryAvailableAsync(ct: ct);

        if (!string.IsNullOrWhiteSpace(options.AuthToken))
        {
            await RegisterAuthTokenAsync(binaryPath, options.AuthToken, ct);
        }

        var session = new NgrokTunnelSession(binaryPath, options);
        session.Start();
        return session;
    }

    private static async Task RegisterAuthTokenAsync(string binaryPath, string authToken, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = $"config add-authtoken {authToken}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch ngrok to configure the authtoken.");

        // Read both pipes concurrently so a full stderr/stdout buffer can't block the child.
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"ngrok rejected the authtoken (exit code {proc.ExitCode}): {detail.Trim()}");
        }
    }
}
