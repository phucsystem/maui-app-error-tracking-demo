---
phase: 5
title: "Performance Tracking"
status: complete
effort: 0.5h
---

# Phase 5 — Performance Tracking

## Overview

Implement `PerformanceService` to record app startup time, WebView page load timings (wall-clock + JS `performance.timing`), and arbitrary custom metrics. All data is written to Crashlytics as custom keys and breadcrumbs — no separate Firebase Performance SDK needed.

## Design Decision

Firebase Performance Monitoring SDK is a separate package (`Plugin.Firebase.Performance`) and adds significant binary size. Given the requirement is metric *visibility* in Crashlytics (not a full APM dashboard), storing metrics as Crashlytics custom keys and breadcrumbs is sufficient and simpler (KISS/YAGNI).

If a proper time-series dashboard is needed later, the `PerformanceService` interface is the single place to swap in Firebase Performance or another APM.

## Files to Create / Modify

| File | Action |
|------|--------|
| `src/Services/PerformanceService.cs` | Create |
| `src/MauiProgram.cs` | Modify — capture startup timestamp |
| `src/Platforms/iOS/AppDelegate.cs` | Modify — record startup complete |

---

## Step 1 — Startup Timestamp Capture

The earliest measurable point in a .NET MAUI iOS app is `Main()`. Add a static timestamp there.

`src/Platforms/iOS/Program.cs` (create if not exists — MAUI iOS entry point):
```csharp
namespace MauiFirebaseMetrics;

// Static startup time — set at process entry before any framework init
internal static class AppStartup
{
    internal static readonly DateTime ProcessStartTime = DateTime.UtcNow;
}

public class Program
{
    static void Main(string[] args)
    {
        UIKit.UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
```

---

## Step 2 — PerformanceService.cs

`src/Services/PerformanceService.cs`:
```csharp
using System.Text.Json;
using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics.Services;

public class PerformanceService
{
    private readonly CrashService _crashService;

    // Keyed by URL — stores most recent load time per URL for trend comparison
    private readonly Dictionary<string, double> _lastPageLoadMs = new();

    public PerformanceService(CrashService crashService)
    {
        _crashService = crashService;
    }

    /// <summary>
    /// Call once after Firebase is initialized and first frame is rendered.
    /// Measures time from process start to usable UI.
    /// </summary>
    public void RecordAppStartup()
    {
        var startupMs = (DateTime.UtcNow - AppStartup.ProcessStartTime).TotalMilliseconds;

        _crashService.Log($"App startup: {startupMs:F0}ms");
        _crashService.SetMetadata("startup_ms", ((long)startupMs).ToString());
    }

    /// <summary>
    /// Called by NavigationDelegate.DidFinishNavigation with wall-clock duration.
    /// </summary>
    public void RecordPageLoad(string url, double wallClockMs)
    {
        _lastPageLoadMs[url] = wallClockMs;

        _crashService.SetMetadata("last_page_load_url", TruncateUrl(url));
        _crashService.SetMetadata("last_page_load_ms", ((long)wallClockMs).ToString());
    }

    /// <summary>
    /// Called by NavigationDelegate with JSON from performance.timing.
    /// Expected keys: domComplete, domInteractive, loadEventEnd (all in ms).
    /// </summary>
    public void RecordJsTiming(string url, string timingJson)
    {
        try
        {
            var timing = JsonSerializer.Deserialize<Dictionary<string, long>>(timingJson);
            if (timing is null) return;

            if (timing.TryGetValue("domComplete", out var domComplete))
                _crashService.SetMetadata("js_dom_complete_ms", domComplete.ToString());

            if (timing.TryGetValue("domInteractive", out var domInteractive))
                _crashService.SetMetadata("js_dom_interactive_ms", domInteractive.ToString());

            if (timing.TryGetValue("loadEventEnd", out var loadEventEnd))
                _crashService.SetMetadata("js_load_event_ms", loadEventEnd.ToString());

            _crashService.Log(
                $"JS timing [{TruncateUrl(url)}]: " +
                $"interactive={domInteractive}ms, complete={domComplete}ms, load={loadEventEnd}ms");
        }
        catch (JsonException ex)
        {
            _crashService.Log($"JS timing parse failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Record any custom duration metric (screen transition, API call, etc.)
    /// </summary>
    public void RecordCustomMetric(string metricName, double durationMs)
    {
        _crashService.SetMetadata(metricName, ((long)durationMs).ToString());
        _crashService.Log($"Metric [{metricName}]: {durationMs:F0}ms");
    }

    // Truncate URL to 128 chars — Crashlytics custom key value limit is 1024 chars,
    // but long URLs pollute the dashboard. Strip query params for cleaner grouping.
    private static string TruncateUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var stripped = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
            return stripped.Length > 128 ? stripped[..128] : stripped;
        }
        catch
        {
            return url.Length > 128 ? url[..128] : url;
        }
    }
}
```

---

## Step 3 — Call RecordAppStartup

In `src/Platforms/iOS/AppDelegate.cs`, after `base.FinishedLaunching` returns (MAUI init complete):

```csharp
public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
{
    App.Configure(); // Firebase init — must be first

    var result = base.FinishedLaunching(application, launchOptions); // MAUI init

    // Record startup time — Firebase and MAUI are now initialized
    var perfService = IPlatformApplication.Current?.Services
        .GetService<PerformanceService>();
    perfService?.RecordAppStartup();

    return result;
}
```

---

## Metrics Reference

All metrics land in Crashlytics under **Keys** tab in the crash/non-fatal report, and as **Breadcrumbs** in the timeline.

| Metric key | Description | Set by |
|-----------|-------------|--------|
| `startup_ms` | Process start → FinishedLaunching complete | `RecordAppStartup` |
| `last_page_load_url` | URL of most recently loaded page (path only) | `RecordPageLoad` |
| `last_page_load_ms` | Wall-clock ms from navigation start to DidFinish | `RecordPageLoad` |
| `js_dom_complete_ms` | `performance.timing.domComplete` | `RecordJsTiming` |
| `js_dom_interactive_ms` | `performance.timing.domInteractive` | `RecordJsTiming` |
| `js_load_event_ms` | `performance.timing.loadEventEnd` | `RecordJsTiming` |
| `last_download_url` | URL of most recent download | `DownloadService` |
| `last_download_duration_ms` | Download wall-clock duration | `DownloadService` |
| `last_download_throughput_kbps` | Average throughput | `DownloadService` |
| `webview_error_code` | NSURLError code of last WebView failure | `NavigationDelegate` |
| `webview_url` | URL where last WebView error occurred | `NavigationDelegate` |

---

## Success Criteria

- [ ] `startup_ms` appears in Crashlytics Keys on first crash/non-fatal report
- [ ] `last_page_load_ms` and `js_dom_complete_ms` recorded after WebView load
- [ ] `RecordCustomMetric` callable for any future screen transition timing
- [ ] Startup metric is captured even if no crash occurs (verify via forced non-fatal)

## Risks

| Risk | Mitigation |
|------|-----------|
| `IPlatformApplication.Current` null in `FinishedLaunching` | Use `MauiContext` DI resolution instead; or inject via constructor if refactoring `AppDelegate` |
| `performance.timing` returns zeros (page loaded from cache) | Non-zero wall-clock from `NavigationDelegate` always present as fallback |
| Crashlytics custom keys overwritten by subsequent navigations | Keys are last-write-wins — breadcrumb log preserves full history per session |
