---
phase: 2
title: "Firebase Crashlytics Integration"
status: complete
effort: 2h
---

# Phase 2 — Firebase Crashlytics Integration

## Overview

Initialize Firebase Crashlytics in .NET MAUI iOS, wire up automatic fatal crash capture, and implement a `CrashService` for manual non-fatal exception reporting.

## Files to Create / Modify

| File | Action |
|------|--------|
| `src/Platforms/iOS/AppDelegate.cs` | Modify — add Firebase init |
| `src/MauiProgram.cs` | Modify — register CrashService, configure Plugin.Firebase |
| `src/Services/CrashService.cs` | Create |

---

## Step 1 — AppDelegate.cs (iOS Firebase Init)

Firebase iOS SDK must be initialized before any other Firebase service. `Plugin.Firebase.Crashlytics` requires `FirebaseApp.Configure()` called in `FinishedLaunching`.

`src/Platforms/iOS/AppDelegate.cs`:
```csharp
using Firebase.Core;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using UIKit;

namespace MauiFirebaseMetrics;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // Firebase must be configured before base.FinishedLaunching
        // so Crashlytics captures crashes during MAUI init
        App.Configure();

        return base.FinishedLaunching(application, launchOptions);
    }
}
```

**Note:** `App.Configure()` reads `GoogleService-Info.plist` automatically — no explicit path needed.

---

## Step 2 — MauiProgram.cs

`src/MauiProgram.cs`:
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using MauiFirebaseMetrics.Services;
using Plugin.Firebase.Crashlytics;

namespace MauiFirebaseMetrics;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseFirebaseCrashlytics();  // Plugin.Firebase.Crashlytics extension

        builder.Services.AddSingleton<CrashService>();
        builder.Services.AddSingleton<PerformanceService>();
        builder.Services.AddSingleton<DownloadService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
```

---

## Step 3 — CrashService.cs

`src/Services/CrashService.cs`:
```csharp
using Plugin.Firebase.Crashlytics;

namespace MauiFirebaseMetrics.Services;

public class CrashService
{
    private readonly IFirebaseCrashlytics _crashlytics;

    public CrashService()
    {
        _crashlytics = CrossFirebaseCrashlytics.Current;

        // Ensure crash collection is enabled (respects user consent patterns)
        _crashlytics.SetCrashlyticsCollectionEnabled(true);
    }

    /// <summary>
    /// Report a handled (non-fatal) exception to Crashlytics.
    /// Call this in catch blocks for errors that don't crash the app.
    /// </summary>
    public void RecordNonFatal(Exception exception, string? context = null)
    {
        if (context is not null)
        {
            _crashlytics.SetCustomKey("error_context", context);
        }

        _crashlytics.RecordException(exception);
    }

    /// <summary>
    /// Attach arbitrary key-value metadata visible in Crashlytics dashboard.
    /// Use for user session info, feature flags, etc.
    /// </summary>
    public void SetMetadata(string key, string value)
    {
        _crashlytics.SetCustomKey(key, value);
    }

    /// <summary>
    /// Log a breadcrumb message visible in the crash report timeline.
    /// </summary>
    public void Log(string message)
    {
        _crashlytics.Log(message);
    }

    /// <summary>
    /// Associate a user ID with crash reports (no PII — use internal ID only).
    /// </summary>
    public void SetUserId(string userId)
    {
        _crashlytics.SetUserId(userId);
    }
}
```

---

## Step 4 — Wire Unhandled Exception Handler

Add to `App.xaml.cs` to capture .NET unhandled exceptions as non-fatal before they become crashes:

`src/App.xaml.cs`:
```csharp
using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics;

public partial class App : Application
{
    private readonly CrashService _crashService;

    public App(CrashService crashService)
    {
        _crashService = crashService;
        InitializeComponent();

        // Catch unhandled .NET exceptions (non-UI-thread)
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Catch unhandled Task exceptions
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        MainPage = new AppShell();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            _crashService.Log($"Unhandled: {exception.Message}");
            // IsTerminating = true means Crashlytics auto-captures as fatal crash
            // IsTerminating = false → record as non-fatal
            if (!args.IsTerminating)
            {
                _crashService.RecordNonFatal(exception, "AppDomain.UnhandledException");
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _crashService.RecordNonFatal(args.Exception, "TaskScheduler.UnobservedTaskException");
        args.SetObserved(); // Prevent process termination for unobserved task exceptions
    }
}
```

---

## Step 5 — Verify Crashlytics Is Active

To confirm setup before moving to Phase 3, add a test crash trigger (remove before release):

```csharp
// Temporary — in MainPage.xaml.cs OnAppearing, remove before production
#if DEBUG
var crashService = Handler?.MauiContext?.Services.GetService<CrashService>();
crashService?.Log("Crashlytics integration verified");
// Uncomment to force a test crash:
// throw new InvalidOperationException("Test crash — Crashlytics verification");
#endif
```

Check Firebase Console → Crashlytics dashboard within 5 minutes of running on device.

---

## Crashlytics Behavior Reference

| Scenario | Captured how |
|----------|-------------|
| Native iOS crash (ObjC/Swift exception) | Auto — Firebase native handler |
| .NET unhandled exception (fatal) | Auto — `UseFirebaseCrashlytics()` installs handler |
| .NET unhandled exception (non-fatal) | `AppDomain.UnhandledException` → `RecordNonFatal` |
| Handled exception (catch block) | Manual — `CrashService.RecordNonFatal(ex)` |
| WebView navigation error | Manual — Phase 3 NavigationDelegate |
| Download error | Manual — Phase 4 DownloadDelegate |

---

## Success Criteria

- [ ] App builds and runs on iOS device/simulator
- [ ] Firebase Console shows app registered and Crashlytics enabled
- [ ] Test log message appears in Crashlytics dashboard
- [ ] `CrashService.RecordNonFatal` can be called from any service without crashing

## Risks

| Risk | Mitigation |
|------|-----------|
| `App.Configure()` called after MAUI init | Must be first line in `FinishedLaunching`, before `base.FinishedLaunching` |
| `GoogleService-Info.plist` missing → silent failure | Build will succeed but Crashlytics silently disabled; add a startup assertion in DEBUG builds |
| `Plugin.Firebase.Crashlytics` 3.x API differences | Verify `CrossFirebaseCrashlytics.Current` is the correct entry point for the installed version |
