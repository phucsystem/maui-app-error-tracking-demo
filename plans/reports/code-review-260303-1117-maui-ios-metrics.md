# Code Review — .NET MAUI iOS Firebase Crashlytics Metrics

**Date:** 2026-03-03
**Plan:** 260303-1122-maui-ios-metrics-init
**Build:** net9.0-ios — 0 errors, 0 warnings

---

## Scope

| File | LOC | Notes |
|------|-----|-------|
| MauiProgram.cs | 62 | DI, Firebase init, handler reg |
| App.xaml.cs | 41 | Unhandled exception wiring |
| MainPage.xaml/.cs | 29 | WebView host |
| Services/CrashService.cs | 39 | Crashlytics facade |
| Services/PerformanceService.cs | 74 | Startup + page load timing |
| Services/DownloadService.cs | 82 | Download lifecycle tracking |
| Platforms/iOS/AppDelegate.cs | 22 | Startup metric trigger |
| Platforms/iOS/Program.cs | 16 | ProcessStartTime capture |
| Platforms/iOS/MetricWebViewHandler.cs | 39 | Custom WKWebView handler |
| Platforms/iOS/NavigationDelegate.cs | 145 | WKNavigationDelegate + JS timing |
| Platforms/iOS/DownloadDelegate.cs | 62 | WKDownloadDelegate |

**Total reviewed:** ~611 LOC

---

## Overall Assessment

Solid greenfield implementation. The architecture is layered cleanly, DI registration is correct, and iOS-specific APIs are used appropriately. The main issues are a thread-safety gap in the service layer dictionaries and a subtle startup-timing inaccuracy. Nothing blocks production use, but two items warrant prompt fixes before load testing at scale.

---

## Critical Issues

None.

---

## High Priority

### H1 — `_activeDownloads` dictionary is not thread-safe

**File:** `Services/DownloadService.cs` — `_activeDownloads` field
**File:** `Services/PerformanceService.cs` — `_lastPageLoadMs` field

Both are plain `Dictionary<string, T>`. `WKDownloadDelegate` callbacks (`DidWriteData`, `DidFinish`, `DidFail`) are dispatched on the WebKit networking thread, not the main thread. `DownloadService` is a singleton, so concurrent downloads (or a progress callback racing a completion callback) will cause a `KeyNotFoundException` or silent corruption.

```csharp
// DownloadService.cs — replace both dictionaries
private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DownloadMetrics>
    _activeDownloads = new();
```

Same fix applies to `_lastPageLoadMs` in `PerformanceService` — concurrent page loads (e.g., iframe + main frame) can interleave writes.

**Impact:** data corruption / crash under concurrent downloads.

---

### H2 — `ProcessStartTime` is captured at static-initialiser time, not true process start

**File:** `Platforms/iOS/Program.cs`, line 7

```csharp
internal static readonly DateTime ProcessStartTime = DateTime.UtcNow;
```

`DateTime.UtcNow` here is stamped when the .NET runtime first touches the `AppStartup` class, which happens well after the iOS process has started (dyld linking, ObjC runtime init, Mono runtime startup). The reported "startup_ms" metric will always undercount by hundreds of milliseconds on a cold launch.

The correct baseline is `NSProcessInfo.ProcessInfo.ProcessStartUptime` (available on iOS 16+):

```csharp
// Platforms/iOS/Program.cs
using Foundation;
internal static class AppStartup
{
    internal static readonly DateTime ProcessStartTime =
        DateTime.UtcNow.AddSeconds(-NSProcessInfo.ProcessInfo.ProcessStartUptime);
}
```

This converts the OS-reported uptime of the process into a UTC wall-clock start time, giving an accurate cold-launch baseline.

**Impact:** misleading startup performance data in Crashlytics.

---

## Medium Priority

### M1 — `CrashService` resolves `CrossFirebaseCrashlytics.Current` twice

**File:** `Services/CrashService.cs` lines 11-12; `MauiProgram.cs` line 59

`CrossFirebaseCrashlytics.Current` is fetched in the `CrashService` constructor **and** registered as a singleton in DI. The DI registration is redundant (nothing injects `IFirebaseCrashlytics` directly), but the constructor resolution is a second call that bypasses DI entirely. This creates a risk that the two instances diverge if the plugin ever changes its singleton semantics.

Fix: inject `IFirebaseCrashlytics` rather than resolving it in the constructor, and rely solely on the DI registration.

```csharp
// Services/CrashService.cs
public CrashService(IFirebaseCrashlytics crashlytics)
{
    _crashlytics = crashlytics;
    _crashlytics.SetCrashlyticsCollectionEnabled(true);
}
```

Remove the `AddSingleton(_ => CrossFirebaseCrashlytics.Current)` line from `MauiProgram.cs` and the `using Plugin.Firebase.Crashlytics` from `CrashService.cs` — it's no longer needed there.

---

### M2 — `DidFailProvisionalNavigation` does not filter error code -999

**File:** `Platforms/iOS/NavigationDelegate.cs` lines 30-41

`DidFailNavigation` correctly ignores error -999 (NSURLErrorCancelled — user or programmatic navigation cancel), but the same guard is absent in `DidFailProvisionalNavigation`. Rapid navigation (typing in a URL bar, redirect chains) will generate -999 in provisional navigation too, creating noise in Crashlytics non-fatal reports.

```csharp
public override void DidFailProvisionalNavigation(
    WKWebView webView, WKNavigation navigation, NSError error)
{
    if (error.Code == -999) return;  // add this guard
    // ... rest unchanged
}
```

---

### M3 — `DownloadDelegate._downloadId` is empty string until `DecideDestination` fires

**File:** `Platforms/iOS/DownloadDelegate.cs`

`_downloadId` and `_sourceUrl` are initialised to `string.Empty`. If `DidWriteData` or `DidFail` fires before `DecideDestination` (unlikely but possible if iOS calls them out of order or if a resume is initiated), `_downloadService.OnDownloadProgress("", ...)` will silently do nothing (the key won't exist in the dictionary), which is acceptable — but `OnDownloadFailed("")` would also silently skip the URL metadata. Log a warning when these are called with an empty ID:

```csharp
public void DidWriteData(...)
{
    if (string.IsNullOrEmpty(_downloadId)) return;
    _downloadService.OnDownloadProgress(_downloadId, totalBytesWritten, totalBytesExpectedToWrite);
}
```

---

### M4 — `RecordJsTimingAsync` uses the deprecated `performance.timing` API

**File:** `Platforms/iOS/NavigationDelegate.cs` lines 113-119

`window.performance.timing` (PerformanceTiming interface) is deprecated in favour of `PerformanceNavigationTiming` (accessible via `performance.getEntriesByType("navigation")[0]`). The legacy API still works in WebKit on iOS 16 as of 2026, but it returns 0 for all fields on cross-origin navigations and on pages that use `history.pushState`. Using the newer API also works in those scenarios:

```javascript
JSON.stringify((function(){
    var e = performance.getEntriesByType("navigation")[0];
    if (!e) return null;
    return {
        domComplete: Math.round(e.domComplete),
        domInteractive: Math.round(e.domInteractive),
        loadEventEnd: Math.round(e.loadEventEnd)
    };
})())
```

The null-check on line 122 (`json != "null"`) already handles the fallback gracefully, so adopting the new API is a low-risk improvement.

---

### M5 — `OnUnhandledException` logs only when `!IsTerminating`, but terminating exceptions also need a log

**File:** `App.xaml.cs` lines 23-33

For a terminating exception (`IsTerminating == true`), only `_crashService.Log(...)` is called — `RecordNonFatal` is skipped. That is intentional to avoid a potential reentrancy crash when the process is about to die, but the log call still reaches Crashlytics, which may not flush before the process exits. Firebase Crashlytics records the fatal crash separately via its own signal handler, so the non-fatal call is correctly omitted. A comment here would prevent a future maintainer from "fixing" it incorrectly:

```csharp
// For terminating exceptions Crashlytics captures the fatal event via its own
// signal handler; calling RecordNonFatal here would be redundant and risky.
if (!args.IsTerminating)
{
    _crashService.RecordNonFatal(exception, "AppDomain.UnhandledException");
}
```

Minor — no functional change needed, just a clarifying comment.

---

## Low Priority

### L1 — `WillFinishLaunching` returns `false` — correct but worth a comment

**File:** `MauiProgram.cs` line 51-55

`WillFinishLaunching` returning `false` tells the system not to continue launch processing — but MAUI's internal `FinishedLaunching` route handles continuation. This is the correct pattern for `Plugin.Firebase` initialisation in MAUI, but it looks wrong on first read. A brief comment would help.

---

### L2 — `MetricWebViewHandler.DisconnectHandler` nulls `NavigationDelegate` with `null!`

**File:** `Platforms/iOS/MetricWebViewHandler.cs` line 35

```csharp
platformView.NavigationDelegate = null!;
```

The `null!` suppression is needed because the property is typed as non-nullable in the binding, but using it acknowledges that null is intentional. Prefer the explicit cast:

```csharp
platformView.NavigationDelegate = (WKNavigationDelegate)(object)null!;
```

Or simply leave it as-is — the intent is clear enough for an iOS developer. Very minor.

---

### L3 — `DownloadDelegate` uses `GetHashCode()` as download ID

**File:** `Platforms/iOS/DownloadDelegate.cs` line 25

```csharp
_downloadId = download.GetHashCode().ToString();
```

`GetHashCode()` on an `NSObject` returns the ObjC `hash`, which is not guaranteed unique across the lifetime of concurrent downloads. Two `WKDownload` instances alive at the same time can theoretically share a hash. A safer alternative:

```csharp
_downloadId = Guid.NewGuid().ToString("N");
```

---

### L4 — `TruncateUrl` swallows all exceptions silently

**File:** `Services/PerformanceService.cs` lines 61-73

The bare `catch` in `TruncateUrl` silently falls back to raw string truncation. This is safe, but logging the parse failure at debug level would help diagnose unexpected URL formats during development:

```csharp
catch (UriFormatException)
{
    return url.Length > 128 ? url[..128] : url;
}
```

Narrowing the catch type also satisfies static analysis tools.

---

### L5 — `last_download_url` metadata is never cleared after download completes

**File:** `Services/DownloadService.cs` line 23

After a download finishes or fails, `last_download_url` retains the value from the last download indefinitely. If a subsequent crash is unrelated to downloads, the stale metadata may mislead investigation. Clear it on completion/failure:

```csharp
_crashService.SetMetadata("last_download_url", string.Empty);
```

---

## Edge Cases Found by Scout

1. **Concurrent navigation + JS timing race:** `RecordJsTimingAsync` captures `url` at `DidFinishNavigation` time, but the `await webView.EvaluateJavaScriptAsync(script)` suspends. If the user navigates again before the await completes, `webView.Url` will have changed but the captured `url` local variable is still the old page's URL — so the JS timing result is correctly attributed to the old URL. No bug, but worth noting: `timingJson` will reflect the *new* page's timing values if the new page already loaded before the script executes. Mitigation: capture both URL and timing in the same script invocation (already partially done via the captured `url` variable).

2. **`_navigationStartTime` is a single field, not keyed per navigation:** If a redirect triggers `DidStartProvisionalNavigation` a second time mid-navigation (e.g., HTTP→HTTPS redirect chain), `_navigationStartTime` is overwritten. The reported wall-clock time will measure only the final redirect hop. For the current use-case (total page-load time) this is acceptable, but if redirect attribution is needed it would require a per-`WKNavigation` timestamp dictionary.

3. **`OnDownloadProgress` 25%-quantisation:** The `percent % 25 == 0` check fires for every byte-write event that happens to land exactly on a 25% boundary. A file that oscillates around 50% (rare but possible with chunked encoding reporting) could log "50%" repeatedly. A `HashSet<int>` of already-logged percentages would prevent duplicates.

4. **`AppDelegate.FinishedLaunching` timing:** `RecordAppStartup` is called after `base.FinishedLaunching`, which is after MAUI's DI container is built. The `PerformanceService` is constructed lazily at this point. If construction throws (unlikely for a pure service), the exception would propagate into `FinishedLaunching` and crash the app silently. Wrap in a try/catch:

```csharp
try { perfService?.RecordAppStartup(); }
catch (Exception ex) { /* log to console at minimum */ _ = ex; }
```

---

## Positive Observations

- Clean three-layer separation: iOS platform layer -> service layer -> Crashlytics facade. No platform code leaks into services.
- `DisconnectHandler` correctly nulls the native delegate before calling `base` — prevents use-after-free during handler recycling.
- `-999` cancellation guard in `DidFailNavigation` is correct and shows awareness of WKWebView quirks.
- `SetCrashlyticsCollectionEnabled(true)` called eagerly in `CrashService` constructor ensures nothing is missed between init and first use.
- `WKWebViewConfiguration` with `AllowsInlineMediaPlayback = true` and `MediaTypesRequiringUserActionForPlayback = None` is appropriate for a health app WebView.
- `GoogleService-Info.plist` is gitignored via the `Condition="Exists(...)"` guard in the csproj — no secrets in source control.
- `WKNavigationResponsePolicy.Download` path is correctly guarded by both `IsDownloadableMimeType` AND `!navigationResponse.CanShowMimeType` — avoids triggering download for renderable PDFs.

---

## Recommended Actions

| Priority | Action |
|----------|--------|
| H1 | Replace `Dictionary` with `ConcurrentDictionary` in `DownloadService` and `PerformanceService` |
| H2 | Replace `DateTime.UtcNow` startup baseline with `NSProcessInfo.ProcessStartUptime` |
| M1 | Inject `IFirebaseCrashlytics` into `CrashService` rather than resolving it directly |
| M2 | Add `if (error.Code == -999) return;` guard to `DidFailProvisionalNavigation` |
| M3 | Guard `DidWriteData`/`DidFail` against empty `_downloadId` |
| M4 | Migrate JS timing script from deprecated `performance.timing` to `PerformanceNavigationTiming` |
| L3 | Replace `GetHashCode()` download ID with `Guid.NewGuid()` |
| L5 | Clear `last_download_url` metadata on download completion/failure |

---

## Metrics

- Nullable annotations: enabled, no suppression beyond one intentional `null!`
- Secrets in source: none
- Linting issues: 0 (build clean)
- Test coverage: n/a (greenfield, no tests in scope)

---

## Unresolved Questions

1. Is `WKNavigationResponsePolicy.Download` available on the iOS 16 minimum, or does it require iOS 14.5? (iOS 14.5 minimum for `WKDownloadDelegate` — covered, but worth confirming the policy enum variant is also 14.5+.)
2. Should `PerformanceService._lastPageLoadMs` dictionary be persisted across sessions (e.g., to NSUserDefaults) or is in-memory-only intentional?
3. The plan's file tree references `Pages/MainPage.xaml` but the actual file sits at `src/MainPage.xaml` (no `Pages/` subdirectory). Is this a pending refactor or was the plan updated late?
4. No `Info.plist` privacy usage descriptions were reviewed — confirm `NSAppTransportSecurity` allows the target WebView domain(s) if not `example.com` in production.
