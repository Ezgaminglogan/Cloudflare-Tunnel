namespace Cloudflare_Tunnel.Core;

public interface IBinaryManager
{
    string GetBinaryDirectory();
    string? FindExistingExecutable(string binaryName);
    Task<string> ExtractEmbeddedBinaryAsync(
        string resourceName,
        string targetFileName,
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default);
    Task<string> DownloadBinaryAsync(
        Uri url,
        string targetFileName,
        IProgress<(long bytesProcessed, long totalBytes, double speedBytesPerSec)>? progress = null,
        CancellationToken ct = default);
}
