import perf, {type FirebasePerformanceTypes} from '@react-native-firebase/perf';
import {crashService} from './crash-service';
import {deviceContextService} from './device-context-service';

const appStartTime = Date.now();

interface NavigationTimingData {
  ttfb: number;
  domInteractive: number;
  domComplete: number;
  loadEventEnd: number;
}

interface ApiTimingData {
  url: string;
  method: string;
  status: number;
  durationMs: number;
  requestSize?: number;
  responseSize?: number;
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
      this.applyDeviceAttributes(trace);
      this.activeTrace = trace;
    } catch (error) {
      crashService.log(`Failed to start perf trace: ${error}`);
    }
  }

  private applyDeviceAttributes(trace: FirebasePerformanceTypes.Trace): void {
    const context = deviceContextService.getContext();
    if (!context) return;

    trace.putAttribute('user_uuid', context.userUuid);
    trace.putAttribute('country_iso', context.countryIso);
    trace.putAttribute('brand', context.brand);
    trace.putAttribute('device_model', context.deviceModel);
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

  async recordApiTiming(timing: ApiTimingData): Promise<void> {
    try {
      const httpMethod = this.toHttpMethod(timing.method);
      const httpMetric = await perf().newHttpMetric(timing.url, httpMethod);

      httpMetric.setHttpResponseCode(timing.status);
      if (timing.requestSize) {
        httpMetric.setRequestPayloadSize(timing.requestSize);
      }
      if (timing.responseSize) {
        httpMetric.setResponsePayloadSize(timing.responseSize);
      }
      httpMetric.setResponseContentType('application/json');

      const context = deviceContextService.getContext();
      if (context) {
        httpMetric.putAttribute('user_uuid', context.userUuid);
        httpMetric.putAttribute('country_iso', context.countryIso);
        httpMetric.putAttribute('brand', context.brand);
        httpMetric.putAttribute('device_model', context.deviceModel);
      }

      await httpMetric.start();
      await httpMetric.stop();

      const truncatedUrl = this.truncateUrl(timing.url);
      crashService.log(
        `API ${timing.method} ${truncatedUrl}: ${timing.status} in ${timing.durationMs}ms`,
      );
    } catch (error) {
      crashService.log(`Failed to record API timing: ${error}`);
    }
  }

  private toHttpMethod(method: string): FirebasePerformanceTypes.HttpMethod {
    const upper = method.toUpperCase();
    const validMethods: FirebasePerformanceTypes.HttpMethod[] = [
      'GET', 'POST', 'PUT', 'DELETE', 'HEAD', 'PATCH', 'OPTIONS', 'TRACE', 'CONNECT',
    ];
    return validMethods.includes(upper as FirebasePerformanceTypes.HttpMethod)
      ? (upper as FirebasePerformanceTypes.HttpMethod)
      : 'GET';
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
