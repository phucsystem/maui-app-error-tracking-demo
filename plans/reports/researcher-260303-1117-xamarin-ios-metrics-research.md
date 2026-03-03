# Xamarin.iOS WebView Metrics & Error Tracking — Research Report

**Date:** 2026-03-03
**Project:** xamarin-web-view-error-trace
**Scope:** Error tracking, performance metrics, SDK options for Xamarin.iOS WebView app

---

## 1. Xamarin.iOS Status in 2025-2026

**VERDICT: Xamarin.iOS is EOL. Continuing on it is a calculated risk.**

| Fact | Detail |
|------|--------|
| Support ended | May 1, 2024 — no security patches, no new APIs |
| Last SDK targets | Xcode 15 / iOS 17, Android API 34 |
| App Store deadline | ~April 2025: Apple stopped accepting builds with Xcode 15 |
| Xcode 16 compatibility | Xamarin.iOS does NOT support Xcode 16+ officially |
| Migration path | Xamarin.iOS → .NET for iOS (part of .NET 8/9) |

**Practical reality for existing apps in 2026:**
- If the app is already in production on Xamarin.iOS, it still *runs* but cannot be updated to the App Store without migration to .NET MAUI / .NET for iOS.
- NuGet packages from the xamarin org are archived/read-only.
- Community forks exist for some libraries.

**Recommendation:** If starting a new project, use **.NET MAUI** or **.NET for iOS**. If maintaining legacy Xamarin.iOS, plan migration to .NET MAUI within 6-12 months. For the purpose of this project (metric tracking tool), consider whether this should target Xamarin.iOS legacy or .NET MAUI from the start.

---

## 2. SDK Options Comparison for Crash/Error/Performance Tracking

### 2.1 Sentry (RECOMMENDED for Xamarin.iOS)

**Why recommended:** Still actively maintained, has Xamarin-specific SDK, covers all required metric categories.

| Attribute | Detail |
|-----------|--------|
| NuGet | `Sentry.Xamarin` 2.1.0 + `Sentry.Xamarin.Forms` 2.1.0 |
| Crash reporting | Yes — fatal + non-fatal (handled exceptions) |
| Performance | Yes — transactions, spans, latency, throughput |
| Breadcrumbs | Auto navigation breadcrumbs via Xamarin.Essentials |
| Network | Via custom instrumentation |
| WebView | Manual integration (inject JS message handlers) |
| Privacy manifest | Auto-patches iOS PrivacyInfo.xcprivacy |
| Status | Active, maintained by getsentry/sentry-xamarin |
| Pricing | Free tier available |

**Setup:**
```csharp
// AppDelegate.FinishedLaunching
SentryXamarin.Init(options =>
{
    options.Dsn = "https://your-dsn@sentry.io/project";
    options.TracesSampleRate = 1.0;
    options.Debug = false;
});
```

**Non-fatal error capture:**
```csharp
try { /* risky code */ }
catch (Exception ex) { SentrySdk.CaptureException(ex); }
```

### 2.2 Firebase Crashlytics (CAUTION — orphaned bindings)

| Attribute | Detail |
|-----------|--------|
| NuGet (official) | `Xamarin.Firebase.iOS.Crashlytics` — **ARCHIVED May 2024** |
| Community fork | `AdamE.Firebase.iOS.Crashlytics` (AdamEssenmacher on GitHub) |
| Crash reporting | Yes — excellent symbolication |
| Performance | Firebase Performance Monitoring (separate SDK, also archived) |
| Non-fatal | Yes — `Crashlytics.SharedInstance.RecordError(exception)` |
| WebView | No native support |
| Status | Official bindings dead; community fork active but risky long-term |

**Risk:** Xamarin binding depends on GoogleApisForiOSComponents, archived by Microsoft. Community fork may lag behind Firebase iOS SDK updates.

**Verdict:** Not recommended for new Xamarin.iOS projects. Use only if team already has Firebase invested.

### 2.3 App Center (DEPRECATED — DO NOT USE)

- **Retired March 31, 2025.** Extended Analytics/Diagnostics to June 2026 via Azure Monitor.
- No new App Center integrations should be started.
- Migration path: Sentry (crash/errors), Azure Pipelines (CI/CD), Bitrise (distribution).

### 2.4 New Relic

| Attribute | Detail |
|-----------|--------|
| NuGet | `NewRelic.Xamarin.Plugin` 1.0.0 |
| Xamarin support ends | **July 2025** — migration to .NET MAUI recommended |
| Crash reporting | Yes |
| Performance | Yes — APM-grade monitoring |
| Network | Yes — HTTP request tracking built-in |
| WebView | Limited |
| Pricing | Enterprise; expensive for small teams |

**Verdict:** If org already uses New Relic APM, viable option. For new projects, Sentry is simpler.

### 2.5 Datadog

| Attribute | Detail |
|-----------|--------|
| SDK | `dd-sdk-ios` — native Swift/ObjC only |
| Xamarin binding | No official Xamarin binding |
| Status | No Xamarin support |

**Verdict:** Skip for Xamarin.iOS. Relevant only post-migration to native .NET for iOS.

### 2.6 Raygun

| Attribute | Detail |
|-----------|--------|
| NuGet | `Mindscape.Raygun4Net.Xamarin.iOS.Unified` |
| Crash reporting | Yes — fatal + native crashes |
| RUM | Yes — `AttachPulse()` for Real User Monitoring |
| Performance | Session timing, screen load |
| WebView | No native support |
| Status | Active, Raygun maintains Xamarin support |
| Pricing | Paid (has trial) |

**Verdict:** Solid alternative to Sentry if budget allows. Better RUM story than Sentry for Xamarin.

---

## 3. SDK Recommendation Matrix

| Need | Best Option | Fallback |
|------|------------|---------|
| Crash reporting (fatal) | Sentry | Raygun |
| Non-fatal exceptions | Sentry | Raygun |
| Performance / startup | Sentry (custom spans) | New Relic |
| Network monitoring | Sentry HttpClientHandler | New Relic |
| WebView errors | Custom WKNavigationDelegate | Custom WKNavigationDelegate |
| WebView load timing | KVO estimatedProgress | JS `performance.timing` via EvaluateJavaScript |
| RUM / session | Raygun Pulse | Sentry |

**Bottom line: Sentry (`Sentry.Xamarin`) is the pragmatic single-SDK choice for Xamarin.iOS in 2026.**

---

## 4. WKWebView Error & Performance Capture

### 4.1 Error Capture — WKNavigationDelegate

WKWebView does NOT fire `WebView.Navigated` on navigation failures (iOS bug, never fixed). Must use custom delegate.

```csharp
public class MetricWebViewDelegate : WKNavigationDelegate
{
    public override void DidFailProvisionalNavigation(
        WKWebView webView, WKNavigation navigation, NSError error)
    {
        // DNS/timeout/network errors before request starts
        // Common codes: -1009 no internet, -1001 timeout, -1003 host not found
        SentrySdk.CaptureMessage(
            $"WebView provisional nav failed: {error.Code} - {error.LocalizedDescription}",
            SentryLevel.Error);
    }

    public override void DidFailNavigation(
        WKWebView webView, WKNavigation navigation, NSError error)
    {
        // Errors after navigation started (HTTP errors, SSL, etc.)
        SentrySdk.CaptureMessage(
            $"WebView nav failed: {error.Code} - {error.LocalizedDescription}",
            SentryLevel.Error);
    }

    public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
    {
        // Track page load complete — measure timing here
    }
}
```

**Common NSURLError codes:**
- `-1009` — No internet (NSURLErrorNotConnectedToInternet)
- `-1001` — Timeout (NSURLErrorTimedOut)
- `-1003` — Host not found (NSURLErrorCannotFindHost)
- `-1100` — URL not found on server
- `-1202` — SSL certificate invalid

### 4.2 Performance Tracking — estimatedProgress KVO

```csharp
// Add KVO observer on WKWebView
webView.AddObserver(this, "estimatedProgress",
    NSKeyValueObservingOptions.New, IntPtr.Zero);

public override void ObserveValue(NSString keyPath, NSObject obj,
    NSDictionary change, IntPtr context)
{
    if (keyPath == "estimatedProgress")
    {
        var progress = webView.EstimatedProgress;
        // 1.0 = fully loaded
        if (progress >= 1.0) RecordPageLoadComplete();
    }
}
```

### 4.3 Network Monitoring Limitation

**Critical limitation:** `NSURLProtocol` does NOT intercept WKWebView network requests — WKWebView runs networking out-of-process.

**Workarounds:**
1. `WKURLSchemeHandler` — only works for custom schemes (not http/https)
2. JavaScript injection: inject `performance.timing` API reader via `EvaluateJavaScriptAsync` after page load
3. Proxy-based interception (complex, not recommended)
4. For download tracking: use `URLSession` directly in native code alongside WebView

**JS timing injection (recommended for page metrics):**
```csharp
// After DidFinishNavigation
var script = @"JSON.stringify({
    domComplete: performance.timing.domComplete - performance.timing.navigationStart,
    loadEvent: performance.timing.loadEventEnd - performance.timing.navigationStart,
    domInteractive: performance.timing.domInteractive - performance.timing.navigationStart
})";
var result = await webView.EvaluateJavaScriptAsync(script);
// Parse result and send to Sentry as custom measurement
```

### 4.4 Heavy File Download Monitoring

Since NSURLProtocol cannot intercept WebView traffic, options are:
1. Use `URLSession` with `downloadTask` for programmatic downloads outside WebView
2. Use `WKScriptMessageHandler` to receive download-start/progress events from JavaScript
3. Track download completion via `DidFinishNavigation` + content-length estimation

---

## 5. Minimal Project Structure

```
xamarin-web-view-error-trace/
├── AppDelegate.cs              # SDK init (Sentry), app lifecycle
├── MainViewController.cs       # Root VC, WebView host
├── WebView/
│   ├── MetricWebView.cs        # WKWebView subclass or wrapper
│   ├── MetricNavigationDelegate.cs  # WKNavigationDelegate impl
│   └── WebViewScriptHandler.cs # WKScriptMessageHandler for JS→native
├── Tracking/
│   ├── CrashTracker.cs         # Fatal/non-fatal error capture
│   ├── PerformanceTracker.cs   # Startup, screen load, custom spans
│   └── NetworkTracker.cs       # Download progress, URLSession wrapper
├── Info.plist
├── GoogleService-Info.plist    # Only if using Firebase
└── PrivacyInfo.xcprivacy       # Required for iOS 17+ App Store
```

---

## 6. NuGet Packages Summary

### Option A: Sentry (Recommended)
```xml
<PackageReference Include="Sentry.Xamarin" Version="2.1.0" />
<!-- If using Xamarin.Forms: -->
<PackageReference Include="Sentry.Xamarin.Forms" Version="2.1.0" />
```

### Option B: Raygun (Alternative, better RUM)
```xml
<PackageReference Include="Mindscape.Raygun4Net.Xamarin.iOS.Unified" Version="[latest]" />
```

### Option C: Firebase Crashlytics (Community fork, risky)
```xml
<PackageReference Include="AdamE.Firebase.iOS.Crashlytics" Version="[latest]" />
<PackageReference Include="AdamE.Firebase.iOS.Core" Version="[latest]" />
```

### Option D: New Relic (Enterprise)
```xml
<PackageReference Include="NewRelic.Xamarin.Plugin" Version="1.0.0" />
<!-- Note: EOL July 2025 for Xamarin target -->
```

### Supporting packages (all options)
```xml
<!-- Xamarin.Essentials for device info, connectivity -->
<PackageReference Include="Xamarin.Essentials" Version="1.8.1" />
```

---

## 7. App Startup Time Measurement (DIY)

If SDK-based startup tracking is insufficient:

```csharp
// In Main.cs — earliest measurable point
static class MainClass
{
    static readonly DateTime AppStartTime = DateTime.UtcNow;

    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}

// In AppDelegate.FinishedLaunching — after SDK init
var startupMs = (DateTime.UtcNow - MainClass.AppStartTime).TotalMilliseconds;
SentrySdk.Metrics.Gauge("app.startup_ms", startupMs);
```

---

## 8. Key Decisions Summary

| Question | Answer |
|----------|--------|
| Xamarin.iOS viable in 2026? | Legacy only — App Store updates blocked without Xcode 16 support |
| Best single SDK? | **Sentry** (`Sentry.Xamarin` 2.1.0) |
| App Center? | Dead — do not use |
| Firebase Crashlytics? | Risky — archived bindings, community fork only |
| WKWebView network intercept? | Impossible via NSURLProtocol; use JS `performance.timing` + WKScriptMessageHandler |
| WebView errors? | `WKNavigationDelegate.DidFailProvisionalNavigation` + `DidFailNavigation` |

---

## Unresolved Questions

1. **Is the target app already deployed on App Store?** If yes, with Xcode 15? This determines urgency of migration vs. new tracking layer.
2. **Is .NET MAUI migration in scope?** If yes, better to build tracking layer in .NET MAUI from day 1.
3. **WebView content origin:** Is the WebView loading internal/local content or external URLs? Affects feasibility of JS injection for timing.
4. **Download definition:** What counts as "heavy file download" — is it triggered by WebView navigation or explicit URLSession calls from native code?
5. **Sentry self-hosted vs. cloud?** Free Sentry.io cloud has event limits; self-hosted is free but ops overhead.
6. **iOS minimum deployment target:** If targeting iOS 15+, some WKWebView APIs (like `WKDownload`) are available natively for download tracking.

---

## Sources

- [Xamarin Official Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/xamarin)
- [Xamarin End of Support FAQs (GitHub)](https://github.com/dotnet/maui/discussions/21214)
- [App Center Retirement (Microsoft)](https://learn.microsoft.com/en-us/appcenter/retirement)
- [Sentry for Xamarin Docs](https://docs.sentry.io/platforms/dotnet/guides/xamarin/)
- [Sentry.Xamarin NuGet](https://www.nuget.org/packages/Sentry.Xamarin)
- [GoogleApisForiOSComponents Community Fork](https://github.com/AdamEssenmacher/GoogleApisForiOSComponents)
- [New Relic Xamarin Plugin](https://docs.newrelic.com/docs/mobile-monitoring/new-relic-mobile-xamarin/monitor-your-xamarin-application/)
- [Raygun Xamarin iOS RUM](https://raygun.com/documentation/language-guides/xamarin/real-user-monitoring/ios/)
- [WKNavigationDelegate.DidFailProvisionalNavigation](https://developer.apple.com/documentation/webkit/wknavigationdelegate/webview(_:didfailprovisionalnavigation:witherror:))
- [NSURLProtocol WKWebView limitation (Apple Forums)](https://developer.apple.com/forums/thread/64240)
- [WKWebView EstimatedProgress (Apple Docs)](https://developer.apple.com/documentation/webkit/wkwebview/1415007-estimatedprogress)
- [Migrating from Xamarin to .NET MAUI 2026 (DEV Community)](https://dev.to/devin-rosario/migrating-from-xamarin-to-dotnet-maui-in-2026-3fbh)
