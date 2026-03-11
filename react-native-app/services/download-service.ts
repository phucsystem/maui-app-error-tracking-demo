import {crashService} from './crash-service';

interface DownloadMetrics {
  url: string;
  expectedBytes: number;
  bytesReceived: number;
  startedAt: number;
}

class DownloadService {
  private activeDownloads: Map<string, DownloadMetrics> = new Map();

  onDownloadStarted(downloadId: string, url: string, expectedBytes?: number): void {
    this.activeDownloads.set(downloadId, {
      url,
      expectedBytes: expectedBytes ?? -1,
      bytesReceived: 0,
      startedAt: Date.now(),
    });

    crashService.log(`Download started: ${url} (expected: ${expectedBytes ?? 'unknown'} bytes)`);
    crashService.setMetadata('last_download_url', url);
  }

  onDownloadProgress(downloadId: string, bytesReceived: number, totalBytes: number): void {
    const metrics = this.activeDownloads.get(downloadId);
    if (!metrics) return;

    metrics.bytesReceived = bytesReceived;

    if (totalBytes > 0) {
      const percent = Math.floor((bytesReceived / totalBytes) * 100);
      if (percent % 25 === 0) {
        crashService.log(`Download progress: ${percent}% (${bytesReceived}/${totalBytes} bytes)`);
      }
    }
  }

  onDownloadCompleted(downloadId: string, _localPath: string): void {
    const metrics = this.activeDownloads.get(downloadId);
    if (!metrics) return;
    this.activeDownloads.delete(downloadId);

    const durationMs = Date.now() - metrics.startedAt;
    const throughputKbps =
      metrics.bytesReceived > 0 && durationMs > 0
        ? (metrics.bytesReceived / 1024) / (durationMs / 1000)
        : 0;

    crashService.log(
      `Download complete: ${metrics.url} — ` +
        `${metrics.bytesReceived} bytes in ${durationMs}ms ` +
        `(${throughputKbps.toFixed(1)} KB/s)`,
    );

    crashService.setMetadata('last_download_duration_ms', String(Math.round(durationMs)));
    crashService.setMetadata('last_download_throughput_kbps', String(Math.round(throughputKbps)));
  }

  onDownloadFailed(downloadId: string, error: Error): void {
    const metrics = this.activeDownloads.get(downloadId);
    if (metrics) {
      this.activeDownloads.delete(downloadId);
      crashService.setMetadata('failed_download_url', metrics.url);
    }

    crashService.recordNonFatal(error, 'DownloadDelegate.DidFail');
  }
}

export const downloadService = new DownloadService();
