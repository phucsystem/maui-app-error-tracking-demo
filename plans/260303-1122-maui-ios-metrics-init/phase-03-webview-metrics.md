---
phase: 3
title: "WebView with Metric Tracking"
status: complete
effort: 2.5h
---

# Phase 3 — WebView with Metric Tracking

## Overview

Build a custom `WKWebView` handler for .NET MAUI that attaches a `WKNavigationDelegate` to capture navigation errors, page load timing, and JS performance metrics. All errors funnel through `CrashService`.

## Technical Constraints

- `NSURLProtocol` cannot intercept WKWebView traffic (out-of-process networking)
- JS injection IS possible for external URLs via `EvaluateJavaScriptAsync` — called after `DidFinishNavigation`
- WKWebView runs in a separate process; timing must be measured at delegate callback boundaries

## Files to Create / Modify

| File | Action |
|------|--------|
| `src/Platforms/iOS/WebViewHandler.cs` | Create — custom MAUI handler |
| `src/Platforms/iOS/NavigationDelegate.cs` | Create — WKNavigationDelegate |
| `src/Pages/MainPage.xaml` | Create — WebView host page |
| `src/Pages/MainPage.xaml.cs` | Create |

---

## Step 1 — NavigationDelegate.cs

`src/Platforms/iOS/NavigationDelegate.cs`:
```csharp
using Foundation;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class NavigationDelegate : WKNavigationDelegate
{
    private readonly CrashService _crashService;
    private readonly PerformanceService _performanceService;
    private DateTime _navigationStartTime;

    public NavigationDelegate(CrashService crashService, PerformanceService performanceService)
    {
        _crashService = crashService;
        _performanceService = performanceService;
    }

    // Called when navigation starts — begin timing
    public override void DidStartProvisionalNavigation(WKWebView webView, WKNavigation navigation)
    {
        _navigationStartTime = DateTime.UtcNow;
        _crashService.Log($"WebView navigation started: {webView.Url?.AbsoluteString}");
    }

    // Errors before request leaves device (DNS, no internet, timeout)
    public override void DidFailProvisionalNavigation(
        WKWebView webView, WKNavigation navigation, NSError error)
    {
        var url = webView.Url?.AbsoluteString ?? "unknown";
        var errorMessage = $"WebView provisional nav failed [{error.Code}]: {error.LocalizedDescription} — {url}";

        _crashService.SetMetadata("webview_error_code", error.Code.ToString());
        _crashService.SetMetadata("webview_url", url);
        _crashService.RecordNonFatal(
            new Exception(errorMessage),
            context: "WKNavigationDelegate.DidFailProvisionalNavigation");
    }

    // Errors after navigation committed (SSL, HTTP-level, server errors)
    public override void DidFailNavigation(
        WKWebView webView, WKNavigation navigation, NSError error)
    {
        var url = webView.Url?.AbsoluteString ?? "unknown";
        var errorMessage = $"WebView nav failed [{error.Code}]: {error.LocalizedDescription} — {url}";

        _crashService.SetMetadata("webview_error_code", error.Code.ToString());
        _crashService.SetMetadata("webview_url", url);
        _crashService.RecordNonFatal(
            new Exception(errorMessage),
            context: "WKNavigationDelegate.DidFailNavigation");
    }

    // Called when page content is fully loaded
    public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
    {
        var wallClockMs = (DateTime.UtcNow - _navigationStartTime).TotalMilliseconds;
        var url = webView.Url?.AbsoluteString ?? "unknown";

        _crashService.Log($"WebView load complete: {url} ({wallClockMs:F0}ms wall-clock)");
        _performanceService.RecordPageLoad(url, wallClockMs);

        // Query browser's own performance.timing for accurate sub-timings
        _ = RecordJsTimingAsync(webView, url);
    }

    // Called when WebView process crashes (distinct from app crash)
    public override void WebContentProcessDidTerminate(WKWebView webView)
    {
        _crashService.RecordNonFatal(
            new Exception("WKWebView content process terminated (OOM or crash)"),
            context: "WebContentProcessDidTerminate");

        // Reload to recover
        webView.Reload();
    }

    private async Task RecordJsTimingAsync(WKWebView webView, string url)
    {
        try
        {
            // performance.timing is deprecated but still widely supported
            // Use performance.getEntriesByType for modern browsers if needed
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
            // JS timing failure is non-critical — log but don't surface to user
            _crashService.Log($"JS timing query failed: {ex.Message}");
        }
    }
}
```

**NSURLError codes reference (for triage):**

| Code | Meaning |
|------|---------|
| -1009 | No internet (NSURLErrorNotConnectedToInternet) |
| -1001 | Timeout (NSURLErrorTimedOut) |
| -1003 | Host not found (NSURLErrorCannotFindHost) |
| -1202 | SSL certificate invalid |
| -999  | Navigation cancelled (user navigated away) — ignore this |

---

## Step 2 — WebViewHandler.cs

Custom MAUI handler injects the `NavigationDelegate` into the native `WKWebView`.

`src/Platforms/iOS/WebViewHandler.cs`:
```csharp
using Microsoft.Maui.Handlers;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class MetricWebViewHandler : WebViewHandler
{
    private NavigationDelegate? _navigationDelegate;

    protected override WKWebView CreatePlatformView()
    {
        var config = new WKWebViewConfiguration();

        // Allow inline media playback (common for external sites)
        config.AllowsInlineMediaPlayback = true;
        config.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;

        return new WKWebView(CGRect.Empty, config);
    }

    protected override void ConnectHandler(WKWebView platformView)
    {
        base.ConnectHandler(platformView);

        var crashService = MauiContext!.Services.GetRequiredService<CrashService>();
        var perfService = MauiContext!.Services.GetRequiredService<PerformanceService>();

        _navigationDelegate = new NavigationDelegate(crashService, perfService);
        platformView.NavigationDelegate = _navigationDelegate;
    }

    protected override void DisconnectHandler(WKWebView platformView)
    {
        platformView.NavigationDelegate = null;
        _navigationDelegate = null;
        base.DisconnectHandler(platformView);
    }
}
```

---

## Step 3 — Register Handler in MauiProgram.cs

Add to `src/MauiProgram.cs` inside `CreateMauiApp()`:

```csharp
builder
    .UseMauiApp<App>()
    .UseFirebaseCrashlytics()
    .ConfigureMauiHandlers(handlers =>
    {
        handlers.AddHandler<WebView, MetricWebViewHandler>();
    });
```

---

## Step 4 — MainPage.xaml

`src/Pages/MainPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="MauiFirebaseMetrics.Pages.MainPage"
             Title="WebMetrics">
    <Grid>
        <WebView x:Name="MainWebView"
                 Source="https://example.com"
                 VerticalOptions="Fill"
                 HorizontalOptions="Fill" />

        <!-- Minimal loading indicator — no extra libraries needed -->
        <ActivityIndicator x:Name="LoadingIndicator"
                           IsRunning="True"
                           IsVisible="True"
                           VerticalOptions="Center"
                           HorizontalOptions="Center" />
    </Grid>
</ContentPage>
```

`src/Pages/MainPage.xaml.cs`:
```csharp
using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics.Pages;

public partial class MainPage : ContentPage
{
    private readonly PerformanceService _performanceService;

    public MainPage(PerformanceService performanceService)
    {
        _performanceService = performanceService;
        InitializeComponent();

        MainWebView.Navigating += OnWebViewNavigating;
        MainWebView.Navigated += OnWebViewNavigated;
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs args)
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs args)
    {
        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;

        // NavigationResult.Failure here means MAUI-layer error (not WKWebView-level)
        // WKWebView-level errors are captured in NavigationDelegate
    }
}
```

---

## Step 5 — AppShell.xaml Route Registration

`src/AppShell.xaml`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:pages="clr-namespace:MauiFirebaseMetrics.Pages"
       x:Class="MauiFirebaseMetrics.AppShell">
    <ShellContent ContentTemplate="{DataTemplate pages:MainPage}"
                  Route="MainPage" />
</Shell>
```

Register `MainPage` with DI in `MauiProgram.cs`:
```csharp
builder.Services.AddTransient<MainPage>();
```

---

## Error Flow Diagram

```
WKWebView navigation
        │
        ├─ DidStartProvisionalNavigation → start timer + breadcrumb
        │
        ├─ DidFailProvisionalNavigation  → CrashService.RecordNonFatal
        │   (DNS / no-internet / timeout)
        │
        ├─ DidFailNavigation             → CrashService.RecordNonFatal
        │   (SSL / server / committed)
        │
        ├─ DidFinishNavigation           → PerformanceService.RecordPageLoad
        │                               → JS performance.timing query
        │
        └─ WebContentProcessDidTerminate → CrashService.RecordNonFatal + Reload
```

---

## Success Criteria

- [ ] Navigating to an invalid URL triggers `DidFailProvisionalNavigation` and appears in Crashlytics as non-fatal
- [ ] Successful page load records timing in `PerformanceService`
- [ ] JS timing JSON is logged to Crashlytics breadcrumbs
- [ ] WKWebView process termination triggers non-fatal report and reloads

## Risks

| Risk | Mitigation |
|------|-----------|
| `MauiContext` null in `ConnectHandler` | Guard with null-check; services registered as singletons so resolution always succeeds |
| `EvaluateJavaScriptAsync` throws on navigation cancel | Wrapped in try/catch — non-critical path |
| `performance.timing` unavailable on some pages (CSP) | Catch exception; wall-clock timing from `_navigationStartTime` is always available as fallback |
| Navigation cancel code -999 floods Crashlytics | Filter `error.Code == -999` in `DidFailNavigation` before recording |
