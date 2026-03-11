using Foundation;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class NavigationDelegate : WKNavigationDelegate
{
    private readonly CrashService _crashService;
    private readonly PerformanceService _performanceService;
    private readonly DownloadService _downloadService;
    private DateTime _navigationStartTime;
    private bool _isInitialLoad = true;

    public event Action? NavigationCompleted;

    public NavigationDelegate(
        CrashService crashService,
        PerformanceService performanceService,
        DownloadService downloadService)
    {
        _crashService = crashService;
        _performanceService = performanceService;
        _downloadService = downloadService;
    }

    public override void DidStartProvisionalNavigation(WKWebView webView, WKNavigation navigation)
    {
        _navigationStartTime = DateTime.UtcNow;
        _crashService.Log($"WebView navigation started: {webView.Url?.AbsoluteString}");
    }

    public override void DidFailProvisionalNavigation(
        WKWebView webView, WKNavigation navigation, NSError error)
    {
        if (error.Code == -999) return;

        var url = webView.Url?.AbsoluteString ?? "unknown";
        var errorMessage = $"WebView provisional nav failed [{error.Code}]: {error.LocalizedDescription} — {url}";

        _crashService.SetMetadata("webview_error_code", error.Code.ToString());
        _crashService.SetMetadata("webview_url", url);
        _crashService.RecordNonFatal(
            new Exception(errorMessage),
            context: "WKNavigationDelegate.DidFailProvisionalNavigation");
        NavigationCompleted?.Invoke();
    }

    public override void DidFailNavigation(
        WKWebView webView, WKNavigation navigation, NSError error)
    {
        // -999 = navigation cancelled by user — not a real error
        if (error.Code == -999) return;

        var url = webView.Url?.AbsoluteString ?? "unknown";
        var errorMessage = $"WebView nav failed [{error.Code}]: {error.LocalizedDescription} — {url}";

        _crashService.SetMetadata("webview_error_code", error.Code.ToString());
        _crashService.SetMetadata("webview_url", url);
        _crashService.RecordNonFatal(
            new Exception(errorMessage),
            context: "WKNavigationDelegate.DidFailNavigation");
        NavigationCompleted?.Invoke();
    }

    public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
    {
        var wallClockMs = (DateTime.UtcNow - _navigationStartTime).TotalMilliseconds;
        var url = webView.Url?.AbsoluteString ?? "unknown";

        _crashService.Log($"WebView load complete: {url} ({wallClockMs:F0}ms wall-clock)");
        _performanceService.RecordPageLoad(url, wallClockMs);

        if (_isInitialLoad)
        {
            _isInitialLoad = false;
            _performanceService.RecordInitialWebViewLoad(wallClockMs);
        }

        NavigationCompleted?.Invoke();

        _ = RecordJsTimingAsync(webView, url);
        _ = InjectImageLoadObserverAsync(webView);
    }

    [Export("webViewWebContentProcessDidTerminate:")]
    public void WebContentProcessDidTerminate(WKWebView webView)
    {
        _crashService.RecordNonFatal(
            new Exception("WKWebView content process terminated (OOM or crash)"),
            context: "WebContentProcessDidTerminate");

        webView.Reload();
    }

    [Export("webView:decidePolicyForNavigationResponse:decisionHandler:")]
    public override void DecidePolicy(
        WKWebView webView,
        WKNavigationResponse navigationResponse,
        Action<WKNavigationResponsePolicy> decisionHandler)
    {
        var mimeType = navigationResponse.Response.MimeType ?? string.Empty;

        if (IsDownloadableMimeType(mimeType) && !navigationResponse.CanShowMimeType)
        {
            decisionHandler(WKNavigationResponsePolicy.Download);
            return;
        }

        decisionHandler(WKNavigationResponsePolicy.Allow);
    }

    public override void NavigationActionDidBecomeDownload(
        WKWebView webView, WKNavigationAction navigationAction, WKDownload download)
    {
        download.Delegate = new DownloadDelegate(_downloadService);
    }

    public override void NavigationResponseDidBecomeDownload(
        WKWebView webView, WKNavigationResponse navigationResponse, WKDownload download)
    {
        download.Delegate = new DownloadDelegate(_downloadService);
    }

    private async Task InjectImageLoadObserverAsync(WKWebView webView)
    {
        try
        {
            const string script = """
                (function() {
                    if (window.__imageObserverInstalled) return;
                    window.__imageObserverInstalled = true;

                    var observer = new PerformanceObserver(function(list) {
                        list.getEntries().forEach(function(entry) {
                            if (entry.initiatorType !== 'img') return;
                            var durationMs = Math.round(entry.duration);
                            var sizeKb = Math.round((entry.transferSize || 0) / 1024);
                            if (durationMs > 500 || sizeKb > 200) {
                                window.webkit.messageHandlers.nativeConsole.postMessage(
                                    JSON.stringify({
                                        level: 'perf',
                                        message: JSON.stringify({
                                            type: 'heavy_image',
                                            url: entry.name,
                                            durationMs: durationMs,
                                            sizeKb: sizeKb,
                                            decodedBodySize: entry.decodedBodySize || 0
                                        })
                                    })
                                );
                            }
                        });
                    });
                    observer.observe({ type: 'resource', buffered: true });
                })();
                """;

            await webView.EvaluateJavaScriptAsync(script);
        }
        catch (Exception ex)
        {
            _crashService.Log($"Image observer injection failed: {ex.Message}");
        }
    }

    private async Task RecordJsTimingAsync(WKWebView webView, string url)
    {
        try
        {
            const string script = """
                JSON.stringify({
                    domComplete: performance.timing.domComplete - performance.timing.navigationStart,
                    domInteractive: performance.timing.domInteractive - performance.timing.navigationStart,
                    loadEventEnd: performance.timing.loadEventEnd - performance.timing.navigationStart
                })
                """;

            var result = await webView.EvaluateJavaScriptAsync(script);
            if (result?.ToString() is string json && json != "null")
            {
                _performanceService.RecordJsTiming(url, json);
            }
        }
        catch (Exception ex)
        {
            _crashService.Log($"JS timing query failed: {ex.Message}");
        }
    }

    private static bool IsDownloadableMimeType(string mimeType) => mimeType switch
    {
        "application/pdf" => true,
        "application/zip" => true,
        "application/x-zip-compressed" => true,
        "application/octet-stream" => true,
        "application/msword" => true,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => true,
        "application/vnd.ms-excel" => true,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => true,
        _ => false
    };
}
