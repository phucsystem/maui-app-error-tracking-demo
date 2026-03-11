import perf, {type FirebasePerformanceTypes} from '@react-native-firebase/perf';
import {crashService} from './crash-service';

const appStartTime = Date.now();

interface NavigationTimingData {
  ttfb: number;
  domInteractive: number;
  domComplete: number;
  loadEventEnd: number;
}

class PerformanceService {
  private lastPageLoadMs: Map<string, number> = new Map();
  private activeTrace: FirebasePerformanceTypes.Trace | null = null;

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

  async startWebViewTrace(url: string): Promise<void> {
    try {
      await this.stopActiveTrace();
      const trace = await perf().startTrace('webview_page_load');
      trace.putAttribute('url', this.truncateUrl(url));
      this.activeTrace = trace;
    } catch (error) {
      crashService.log(`Failed to start perf trace: ${error}`);
    }
  }

  async recordNavigationTiming(url: string, timingData: NavigationTimingData): Promise<void> {
    try {
      if (this.activeTrace) {
        this.activeTrace.putMetric('ttfb_ms', Math.round(timingData.ttfb));
        this.activeTrace.putMetric('dom_interactive_ms', Math.round(timingData.domInteractive));
        this.activeTrace.putMetric('dom_complete_ms', Math.round(timingData.domComplete));
        this.activeTrace.putMetric('total_load_ms', Math.round(timingData.loadEventEnd));
        await this.activeTrace.stop();
        this.activeTrace = null;
      }

      crashService.setMetadata('js_ttfb_ms', String(Math.round(timingData.ttfb)));
      crashService.setMetadata('js_dom_interactive_ms', String(Math.round(timingData.domInteractive)));
      crashService.setMetadata('js_dom_complete_ms', String(Math.round(timingData.domComplete)));
      crashService.setMetadata('js_load_event_ms', String(Math.round(timingData.loadEventEnd)));

      const truncatedUrl = this.truncateUrl(url);
      crashService.log(
        `Nav timing [${truncatedUrl}]: ` +
          `ttfb=${Math.round(timingData.ttfb)}ms, ` +
          `interactive=${Math.round(timingData.domInteractive)}ms, ` +
          `complete=${Math.round(timingData.domComplete)}ms, ` +
          `load=${Math.round(timingData.loadEventEnd)}ms`,
      );
    } catch (error) {
      crashService.log(`Failed to record nav timing: ${error}`);
    }
  }

  async stopActiveTrace(): Promise<void> {
    if (this.activeTrace) {
      try {
        await this.activeTrace.stop();
      } catch {
        // Trace already stopped or invalid
      }
      this.activeTrace = null;
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
