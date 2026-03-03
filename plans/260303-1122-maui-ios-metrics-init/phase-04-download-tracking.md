---
phase: 4
title: "Download Tracking"
status: complete
effort: 1.5h
---

# Phase 4 — Download Tracking

## Overview

Detect and monitor heavy file downloads triggered by WebView navigation. iOS 14.5+ provides `WKDownloadDelegate` for native download interception. We capture download start, progress, completion, and failure — all reported through `CrashService` and `PerformanceService`.

## Technical Background

When a user taps a download link in WKWebView (PDF, ZIP, etc.), WKWebView fires `DecidePolicy` with a `WKNavigationResponsePolicy`. iOS 14.5+ added `WKDownload` — a proper download API with progress callbacks. Since iOS 16 is the minimum target, `WKDownload` is fully available.

**Flow:**
```
User taps link
      │
WKNavigationDelegate.DecidePolicy(for response)
      │
      ├─ MIME type is downloadable? → .download → WKDownloadDelegate created
      │
      └─ Normal navigation? → .allow → page loads normally
```

## Files to Create / Modify

| File | Action |
|------|--------|
| `src/Platforms/iOS/NavigationDelegate.cs` | Modify — add `DecidePolicy` for response |
| `src/Platforms/iOS/DownloadDelegate.cs` | Create — WKDownloadDelegate |
| `src/Services/DownloadService.cs` | Create |

---

## Step 1 — DownloadService.cs

`src/Services/DownloadService.cs`:
```csharp
using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics.Services;

public class DownloadService
{
    private readonly CrashService _crashService;

    // Track in-progress downloads: downloadId → metadata
    private readonly Dictionary<string, DownloadMetrics> _activeDownloads = new();

    public DownloadService(CrashService crashService)
    {
        _crashService = crashService;
    }

    public void OnDownloadStarted(string downloadId, string url, long? expectedBytes)
    {
        _activeDownloads[downloadId] = new DownloadMetrics
        {
            Url = url,
            ExpectedBytes = expectedBytes ?? -1,
            StartedAt = DateTime.UtcNow
        };

        _crashService.Log($"Download started: {url} (expected: {expectedBytes?.ToString() ?? "unknown"} bytes)");
        _crashService.SetMetadata("last_download_url", url);
    }

    public void OnDownloadProgress(string downloadId, long bytesReceived, long totalBytes)
    {
        if (!_activeDownloads.TryGetValue(downloadId, out var metrics))
            return;

        metrics.BytesReceived = bytesReceived;

        // Log at 25% intervals to avoid flooding breadcrumbs
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
        if (!_activeDownloads.TryGetValue(downloadId, out var metrics))
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

        _activeDownloads.Remove(downloadId);
    }

    public void OnDownloadFailed(string downloadId, Exception error)
    {
        if (_activeDownloads.TryGetValue(downloadId, out var metrics))
        {
            _crashService.SetMetadata("failed_download_url", metrics.Url);
            _activeDownloads.Remove(downloadId);
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
```

---

## Step 2 — DownloadDelegate.cs

`src/Platforms/iOS/DownloadDelegate.cs`:
```csharp
using Foundation;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class DownloadDelegate : WKDownloadDelegate
{
    private readonly DownloadService _downloadService;
    private string _downloadId = string.Empty;
    private string _sourceUrl = string.Empty;

    public DownloadDelegate(DownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    // Called immediately when download is created — decide where to save it
    public override void DecideDestination(
        WKDownload download,
        NSUrlResponse response,
        string suggestedFilename,
        Action<NSUrl?> completionHandler)
    {
        _downloadId = download.GetHashCode().ToString();
        _sourceUrl = response.Url?.AbsoluteString ?? "unknown";

        var expectedBytes = response.ExpectedContentLength > 0
            ? (long?)response.ExpectedContentLength
            : null;

        _downloadService.OnDownloadStarted(_downloadId, _sourceUrl, expectedBytes);

        // Save to app's temp directory — adjust to Documents if persistence needed
        var tempDir = NSFileManager.DefaultManager.GetTemporaryDirectory();
        var destinationUrl = tempDir.Append(suggestedFilename, false);

        completionHandler(destinationUrl);
    }

    // Called repeatedly during download for progress updates
    public override void DidReceiveData(WKDownload download, long bytesWritten, long totalBytesWritten, long totalBytesExpectedToWrite)
    {
        _downloadService.OnDownloadProgress(_downloadId, totalBytesWritten, totalBytesExpectedToWrite);
    }

    // Called on successful completion
    public override void DidFinish(WKDownload download)
    {
        _downloadService.OnDownloadCompleted(_downloadId, localPath: string.Empty);
    }

    // Called on failure — resume data available for retry if needed
    public override void DidFail(WKDownload download, NSError error, NSData? resumeData)
    {
        var exception = new Exception(
            $"Download failed [{error.Code}]: {error.LocalizedDescription} — {_sourceUrl}");
        _downloadService.OnDownloadFailed(_downloadId, exception);
    }
}
```

---

## Step 3 — DecidePolicy in NavigationDelegate.cs

Add these two methods to `src/Platforms/iOS/NavigationDelegate.cs`:

```csharp
// Add field to NavigationDelegate class:
private readonly DownloadService _downloadService;

// Update constructor:
public NavigationDelegate(
    CrashService crashService,
    PerformanceService performanceService,
    DownloadService downloadService)
{
    _crashService = crashService;
    _performanceService = performanceService;
    _downloadService = downloadService;
}

// Decide policy for navigation response — intercept downloadable MIME types
public override void DecidePolicy(
    WKWebView webView,
    WKNavigationResponse navigationResponse,
    Action<WKNavigationResponsePolicy> decisionHandler)
{
    var mimeType = navigationResponse.Response.MimeType ?? string.Empty;

    if (IsDownloadableMimeType(mimeType) && !navigationResponse.CanShowMIMEType)
    {
        // iOS 14.5+: trigger WKDownload pipeline
        decisionHandler(WKNavigationResponsePolicy.Download);
        return;
    }

    decisionHandler(WKNavigationResponsePolicy.Allow);
}

// Called by WKWebView when download is created from DecidePolicy(.Download)
public override void NavigationAction(
    WKWebView webView,
    WKDownload download)
{
    download.Delegate = new DownloadDelegate(_downloadService);
}

public override void NavigationResponse(
    WKWebView webView,
    WKDownload download)
{
    download.Delegate = new DownloadDelegate(_downloadService);
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
```

---

## Step 4 — Update WebViewHandler.cs

Update `ConnectHandler` in `src/Platforms/iOS/WebViewHandler.cs` to inject `DownloadService`:

```csharp
protected override void ConnectHandler(WKWebView platformView)
{
    base.ConnectHandler(platformView);

    var crashService = MauiContext!.Services.GetRequiredService<CrashService>();
    var perfService = MauiContext!.Services.GetRequiredService<PerformanceService>();
    var downloadService = MauiContext!.Services.GetRequiredService<DownloadService>();

    _navigationDelegate = new NavigationDelegate(crashService, perfService, downloadService);
    platformView.NavigationDelegate = _navigationDelegate;
}
```

---

## Download Detection Decision Tree

```
Response received
      │
      ├─ MIME type is downloadable?
      │         AND canShowMIMEType = false?
      │         │
      │         YES → DecidePolicy(.Download)
      │               └─ WKDownload created
      │                     └─ DownloadDelegate.DecideDestination
      │                           → OnDownloadStarted
      │                           → DidReceiveData (progress)
      │                           → DidFinish / DidFail
      │
      └─ NO → DecidePolicy(.Allow) — normal page load
```

---

## Success Criteria

- [ ] Tapping a PDF/ZIP link in WebView creates a `WKDownload` (not a blank page)
- [ ] `DownloadService.OnDownloadStarted` fires with correct URL and expected size
- [ ] Progress callbacks fire at 25% intervals in Crashlytics breadcrumbs
- [ ] Completed download logged with duration and throughput in Crashlytics
- [ ] Failed download recorded as non-fatal in Crashlytics

## Risks

| Risk | Mitigation |
|------|-----------|
| `WKNavigationResponsePolicy.Download` enum value missing in binding | Verify `Plugin.Firebase` version includes latest WebKit bindings; raw value is `2` if needed |
| Server returns `application/octet-stream` for non-download content | `!navigationResponse.CanShowMIMEType` guard prevents false positives |
| Large downloads fill temp directory | Implement cleanup in `DidFinish` — move to Documents or delete after processing |
| `NavigationAction`/`NavigationResponse` WKDownload callbacks — binding API may differ | Consult `Plugin.Firebase` / .NET MAUI WebKit binding docs; fallback: use `WKNavigationDelegate` `Download` property directly |
