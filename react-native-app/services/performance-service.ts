import {crashService} from './crash-service';

const appStartTime = Date.now();

class PerformanceService {
  private lastPageLoadMs: Map<string, number> = new Map();

  recordAppStartup(): void {
    const startupMs = Date.now() - appStartTime;
    crashService.log(`App startup: ${startupMs}ms`);
    crashService.setMetadata('startup_ms', String(startupMs));
  }

  recordInitialWebViewLoad(wallClockMs: number): void {
    const totalFromStartMs = Date.now() - appStartTime;
    crashService.setMetadata('webview_init_load_ms', String(Math.round(wallClockMs)));
    crashService.setMetadata('webview_init_total_ms', String(totalFromStartMs));
    crashService.log(
      `Initial WebView load: ${Math.round(wallClockMs)}ms (total from app start: ${totalFromStartMs}ms)`,
    );
  }

  recordHeavyImageLoad(imageUrl: string, durationMs: number, sizeKb: number): void {
    const truncatedUrl = this.truncateUrl(imageUrl);
    crashService.setMetadata('heavy_image_url', truncatedUrl);
    crashService.setMetadata('heavy_image_duration_ms', String(durationMs));
    crashService.setMetadata('heavy_image_size_kb', String(sizeKb));
    crashService.log(`Heavy image: ${sizeKb}KB in ${durationMs}ms — ${truncatedUrl}`);
  }

  recordPageLoad(url: string, wallClockMs: number): void {
    this.lastPageLoadMs.set(url, wallClockMs);
    crashService.setMetadata('last_page_load_url', this.truncateUrl(url));
    crashService.setMetadata('last_page_load_ms', String(Math.round(wallClockMs)));
  }

  recordJsTiming(url: string, timingJson: string): void {
    try {
      const timing = JSON.parse(timingJson);
      if (!timing) return;

      if (timing.domComplete !== undefined) {
        crashService.setMetadata('js_dom_complete_ms', String(timing.domComplete));
      }
      if (timing.domInteractive !== undefined) {
        crashService.setMetadata('js_dom_interactive_ms', String(timing.domInteractive));
      }
      if (timing.loadEventEnd !== undefined) {
        crashService.setMetadata('js_load_event_ms', String(timing.loadEventEnd));
      }

      crashService.log(
        `JS timing [${this.truncateUrl(url)}]: ` +
          `interactive=${timing.domInteractive}ms, complete=${timing.domComplete}ms, load=${timing.loadEventEnd}ms`,
      );
    } catch (error) {
      crashService.log(`JS timing parse failed: ${error}`);
    }
  }

  recordCustomMetric(metricName: string, durationMs: number): void {
    crashService.setMetadata(metricName, String(Math.round(durationMs)));
    crashService.log(`Metric [${metricName}]: ${Math.round(durationMs)}ms`);
  }

  private truncateUrl(url: string): string {
    try {
      const parsed = new URL(url);
      const stripped = `${parsed.protocol}//${parsed.host}${parsed.pathname}`;
      return stripped.length > 128 ? stripped.substring(0, 128) : stripped;
    } catch {
      return url.length > 128 ? url.substring(0, 128) : url;
    }
  }
}

export const performanceService = new PerformanceService();
