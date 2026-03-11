using System.Collections.Concurrent;

namespace MauiFirebaseMetrics.Services;

public class DownloadService
{
    private readonly CrashService _crashService;
    private readonly ConcurrentDictionary<string, DownloadMetrics> _activeDownloads = new();

    public DownloadService(CrashService crashService)
    {
        _crashService = crashService;
    }

    public void OnDownloadStarted(string downloadId, string url, long? expectedBytes)
    {
        _activeDownloads.TryAdd(downloadId, new DownloadMetrics
        {
            Url = url,
            ExpectedBytes = expectedBytes ?? -1,
            StartedAt = DateTime.UtcNow
        });

        _crashService.Log($"Download started: {url} (expected: {expectedBytes?.ToString() ?? "unknown"} bytes)");
        _crashService.SetMetadata("last_download_url", url);
    }

    public void OnDownloadProgress(string downloadId, long bytesReceived, long totalBytes)
    {
        if (!_activeDownloads.TryGetValue(downloadId, out var metrics))
            return;

        metrics.BytesReceived = bytesReceived;

        if (totalBytes > 0)
        {
            var percent = (int)((double)bytesReceived / totalBytes * 100);
            if (percent % 25 == 0)
            {
                _crashService.Log($"Download progress: {percent}% ({bytesReceived}/{totalBytes} bytes)");
            }
        }
    }

    public void OnDownloadCompleted(string downloadId, string localPath)
    {
        if (!_activeDownloads.TryRemove(downloadId, out var metrics))
            return;

        var durationMs = (DateTime.UtcNow - metrics.StartedAt).TotalMilliseconds;
        var throughputKbps = metrics.BytesReceived > 0 && durationMs > 0
            ? (metrics.BytesReceived / 1024.0) / (durationMs / 1000.0)
            : 0;

        _crashService.Log(
            $"Download complete: {metrics.Url} — " +
            $"{metrics.BytesReceived} bytes in {durationMs:F0}ms " +
            $"({throughputKbps:F1} KB/s)");

        _crashService.SetMetadata("last_download_duration_ms", ((long)durationMs).ToString());
        _crashService.SetMetadata("last_download_throughput_kbps", ((long)throughputKbps).ToString());
    }

    public void OnDownloadFailed(string downloadId, Exception error)
    {
        if (_activeDownloads.TryRemove(downloadId, out var metrics))
        {
            _crashService.SetMetadata("failed_download_url", metrics.Url);
        }

        _crashService.RecordNonFatal(error, context: "WKDownloadDelegate.DidFail");
    }

    private sealed class DownloadMetrics
    {
        public required string Url { get; set; }
        public long ExpectedBytes { get; set; }
        public long BytesReceived { get; set; }
        public DateTime StartedAt { get; set; }
    }
}
