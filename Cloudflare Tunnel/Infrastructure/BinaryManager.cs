using System.Diagnostics;
using System.Reflection;
using Cloudflare_Tunnel.Core;

namespace Cloudflare_Tunnel.Infrastructure;

public class BinaryManager : IBinaryManager
{
    private readonly string _storageDir;

    public BinaryManager()
    {
        _storageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UniversalTunnel",
            "bin"
        );

        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }
    }

    public string GetBinaryDirectory() => _storageDir;

    public string? FindExistingExecutable(string binaryName)
    {
        if (!binaryName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
        {
            binaryName += ".exe";
        }

        // 1. Check our dedicated app data directory
        string localPath = Path.Combine(_storageDir, binaryName);
        if (File.Exists(localPath))
        {
            return localPath;
        }

        // 2. Check local application base directory
        string baseDir = AppContext.BaseDirectory;
        string appBasePath = Path.Combine(baseDir, binaryName);
        if (File.Exists(appBasePath))
        {
            return appBasePath;
        }

        // 3. Check system PATH
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(part.Trim(), binaryName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignore invalid path entries
                }
            }
        }

        return null;
    }

    public async Task<string> ExtractEmbeddedBinaryAsync(
        string resourceName,
        string targetFileName,
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default)
    {
        if (!targetFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
        {
            targetFileName += ".exe";
        }

        string targetPath = Path.Combine(_storageDir, targetFileName);

        var assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            // Fallback: check if resource name is slightly different
            string[] names = assembly.GetManifestResourceNames();
            string? match = names.FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'. " +
                    $"Available resources: {string.Join(", ", names)}");
            }
            return await ExtractStreamAsync(assembly.GetManifestResourceStream(match)!, targetPath, progress, ct);
        }

        return await ExtractStreamAsync(stream, targetPath, progress, ct);
    }

    private static async Task<string> ExtractStreamAsync(
        Stream stream,
        string targetPath,
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress,
        CancellationToken ct)
    {
        long totalBytes = stream.Length;

        // If target file already exists and size matches, report 100% and skip redundant I/O
        if (File.Exists(targetPath))
        {
            var info = new FileInfo(targetPath);
            if (info.Length == totalBytes)
            {
                progress?.Report((totalBytes, totalBytes, 0));
                return targetPath;
            }
        }

        string tempPath = targetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); } catch { }
        }

        var sw = Stopwatch.StartNew();
        long bytesProcessed = 0;
        byte[] buffer = new byte[128 * 1024]; // 128 KB buffer

        await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                bytesProcessed += bytesRead;
                double speed = sw.Elapsed.TotalSeconds > 0.05 ? bytesProcessed / sw.Elapsed.TotalSeconds : 0;
                progress?.Report((bytesProcessed, totalBytes, speed));
            }
        }

        if (File.Exists(targetPath))
        {
            try { File.Delete(targetPath); } catch { }
        }

        File.Move(tempPath, targetPath);
        return targetPath;
    }

    public async Task<string> DownloadBinaryAsync(
        Uri url,
        string targetFileName,
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default)
    {
        if (!targetFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
        {
            targetFileName += ".exe";
        }

        string targetPath = Path.Combine(_storageDir, targetFileName);
        string tempPath = targetPath + ".download";

        using var client = new HttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

        var sw = Stopwatch.StartNew();
        long bytesProcessed = 0;
        byte[] buffer = new byte[128 * 1024];

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesProcessed += bytesRead;
            double speed = sw.Elapsed.TotalSeconds > 0.05 ? bytesProcessed / sw.Elapsed.TotalSeconds : 0;
            progress?.Report((bytesProcessed, totalBytes, speed));
        }

        if (File.Exists(targetPath))
        {
            try { File.Delete(targetPath); } catch { }
        }

        File.Move(tempPath, targetPath);
        return targetPath;
    }
}
