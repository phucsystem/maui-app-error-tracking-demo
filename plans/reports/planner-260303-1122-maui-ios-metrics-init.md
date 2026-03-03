---
type: planner-report
date: 2026-03-03
slug: maui-ios-metrics-init
plan: plans/260303-1122-maui-ios-metrics-init/
---

# Planner Report — .NET MAUI iOS Firebase Crashlytics Metrics Init

## Decision Log

### SDK Choice: Plugin.Firebase.Crashlytics (not AdamE fork)

Research report flagged `AdamE.Firebase.iOS.Crashlytics` as risky (archived bindings lineage). The actively maintained .NET MAUI path is `Plugin.Firebase.Crashlytics` by TobiasBuchholz — wraps the native Firebase iOS SDK 11.x, supports .NET 9 MAUI, and has a proper MAUI extension method (`UseFirebaseCrashlytics()`).

### No Firebase Performance SDK

Firebase Performance Monitoring is a separate NuGet (`Plugin.Firebase.Performance`). For the stated requirement (visibility of metrics in Crashlytics), storing metrics as Crashlytics custom keys + breadcrumbs is sufficient and avoids an extra SDK dependency. `PerformanceService` encapsulates all metric recording — swap point is clean if APM dashboard is needed later.

### iOS 16 minimum → WKDownloadDelegate fully available

`WKDownload` / `WKDownloadDelegate` was introduced in iOS 14.5. Setting minimum deployment to iOS 16 removes any conditional compilation guards — the download interception path is always active.

### NSURLProtocol cannot intercept WKWebView traffic

This is an Apple architectural constraint (WebKit runs out-of-process). The plan addresses this via:
1. `WKNavigationDelegate` for all navigation-level errors
2. `EvaluateJavaScriptAsync` + `performance.timing` for in-page load metrics
3. `WKDownloadDelegate` via `DecidePolicy(.Download)` for file downloads

JS injection works on external URLs because we call `EvaluateJavaScriptAsync` *after* `DidFinishNavigation` — we're reading timing data, not injecting behaviour.

---

## Architecture Summary

```
MauiProgram.cs
  └─ UseFirebaseCrashlytics()
  └─ AddHandler<WebView, MetricWebViewHandler>()
  └─ DI: CrashService, PerformanceService, DownloadService

AppDelegate.FinishedLaunching
  ├─ App.Configure()          ← Firebase iOS init (must be first)
  ├─ base.FinishedLaunching() ← MAUI init
  └─ PerformanceService.RecordAppStartup()

MetricWebViewHandler (iOS handler)
  └─ NavigationDelegate
        ├─ DidStartProvisionalNavigation → start timer
        ├─ DidFailProvisionalNavigation  → CrashService.RecordNonFatal
        ├─ DidFailNavigation             → CrashService.RecordNonFatal
        ├─ DidFinishNavigation           → PerformanceService.RecordPageLoad
        │                               → EvaluateJavaScriptAsync (JS timing)
        ├─ WebContentProcessDidTerminate → RecordNonFatal + Reload
        └─ DecidePolicy(response)        → DownloadDelegate if downloadable MIME

DownloadDelegate (WKDownloadDelegate)
  └─ DecideDestination → DownloadService.OnDownloadStarted
  └─ DidReceiveData    → DownloadService.OnDownloadProgress
  └─ DidFinish         → DownloadService.OnDownloadCompleted
  └─ DidFail           → DownloadService.OnDownloadFailed → RecordNonFatal

App.xaml.cs
  └─ AppDomain.UnhandledException    → RecordNonFatal (non-terminating)
  └─ TaskScheduler.UnobservedTaskException → RecordNonFatal
```

---

## Phase Summary

| Phase | Files | Est. | Key Decisions |
|-------|-------|------|--------------|
| 1 — Scaffold | `.csproj`, `Info.plist`, `PrivacyInfo.xcprivacy`, plist template | 1.5h | iOS-only MAUI, .NET 9, iOS 16 min |
| 2 — Crashlytics | `AppDelegate.cs`, `MauiProgram.cs`, `CrashService.cs`, `App.xaml.cs` | 2h | `App.Configure()` before MAUI init |
| 3 — WebView | `NavigationDelegate.cs`, `WebViewHandler.cs`, `MainPage.xaml` | 2.5h | Custom handler replaces default MAUI WebView handler |
| 4 — Downloads | `DownloadDelegate.cs`, `DownloadService.cs`, NavigationDelegate additions | 1.5h | `DecidePolicy(.Download)` + `WKDownloadDelegate` |
| 5 — Performance | `PerformanceService.cs`, `Program.cs` startup timestamp | 0.5h | Static `ProcessStartTime` at `Main()` entry |

**Total estimate: 8h**

---

## Unresolved Questions

1. **`Plugin.Firebase.Crashlytics` exact version for .NET 9** — Verify latest 3.x on NuGet supports `net9.0-ios` TFM. At plan time, 3.0.2 is referenced; confirm on https://www.nuget.org/packages/Plugin.Firebase.Crashlytics before starting Phase 1.

2. **`DecidePolicy` + `WKDownload` MAUI binding API** — The .NET MAUI WebKit binding method names for `NavigationAction(WKWebView, WKDownload)` and `NavigationResponse(WKWebView, WKDownload)` may differ from the Obj-C names. Verify against the actual binding headers (`WebKit.WKNavigationDelegate`) in the installed .NET iOS workload before implementing Phase 4.

3. **`IPlatformApplication.Current` availability in `FinishedLaunching`** — DI container may not be fully built at the point `RecordAppStartup` is called. If null, refactor to resolve `PerformanceService` from `App.Current.Handler.MauiContext` in the first `OnAppearing` of `MainPage` instead.

4. **App Store `NSAllowsArbitraryLoads`** — Listed as `true` in `Info.plist` for convenience. If target URLs are a known set, replace with `NSExceptionDomains` for each domain before App Store submission.

5. **Download file lifecycle** — Phase 4 saves downloads to the temp directory. If downloaded files need to be opened in-app (PDFs via `WKWebView.LoadFileUrl`, etc.), a file-management step is needed and not currently planned.
