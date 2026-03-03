---
title: ".NET MAUI iOS — Firebase Crashlytics Metrics Init"
description: "Greenfield .NET MAUI iOS app with Firebase Crashlytics crash/non-fatal reporting, WebView error capture, and download/perf tracking."
status: complete
priority: P1
effort: 8h
branch: main
tags: [maui, ios, firebase, crashlytics, webview, metrics]
created: 2026-03-03
---

# .NET MAUI iOS — Firebase Crashlytics Metrics Initialization

## Context

- **Project:** xamarin-web-view-error-trace (greenfield — legacy name, .NET MAUI)
- **Target:** iOS 16+, .NET 9 MAUI
- **SDK:** Firebase Crashlytics via `Plugin.Firebase.Crashlytics` (community .NET MAUI fork)
- **Research:** [researcher report](../reports/researcher-260303-1117-xamarin-ios-metrics-research.md)

## SDK Decision

**Firebase Crashlytics** via `Plugin.Firebase.Crashlytics` (cross-platform .NET MAUI community package — actively maintained by TobiasBuchholz, distinct from archived AdamE fork).

Rationale: user explicitly requires Firebase Crashlytics. The `Plugin.Firebase` family supports .NET MAUI natively and is the most actively maintained path in 2026.

## Architecture Overview

```
MauiProgram.cs          → Firebase init, DI registration
AppShell.xaml           → Navigation host
Pages/MainPage.xaml     → WebView host page
Services/
  CrashService.cs       → Fatal/non-fatal error reporting
  PerformanceService.cs → Startup timing, custom metrics
  DownloadService.cs    → URLSession download tracking
Platforms/iOS/
  WebViewHandler.cs     → WKWebView custom handler
  NavigationDelegate.cs → WKNavigationDelegate
  DownloadDelegate.cs   → WKDownloadDelegate (iOS 14.5+)
  AppDelegate.cs        → Firebase iOS init
```

## Phases

| # | Phase | Est. | Status |
|---|-------|------|--------|
| 1 | [Project Scaffold](phase-01-project-scaffold.md) | 1.5h | complete |
| 2 | [Firebase Crashlytics Integration](phase-02-firebase-crashlytics.md) | 2h | complete |
| 3 | [WebView with Metric Tracking](phase-03-webview-metrics.md) | 2.5h | complete |
| 4 | [Download Tracking](phase-04-download-tracking.md) | 1.5h | complete |
| 5 | [Performance Tracking](phase-05-performance-tracking.md) | 0.5h | complete |

## Key Dependencies

- `Plugin.Firebase.Crashlytics` requires `GoogleService-Info.plist` with Crashlytics enabled
- WKDownloadDelegate requires iOS 14.5+ (covered by iOS 16 minimum)
- Download tracking depends on Phase 3 WebView handler being in place
- Phase 5 startup timing requires Phase 2 Crashlytics init

## File Tree (Target)

```
src/
├── MauiProgram.cs
├── AppShell.xaml / .cs
├── Pages/
│   └── MainPage.xaml / .cs
├── Services/
│   ├── CrashService.cs
│   ├── PerformanceService.cs
│   └── DownloadService.cs
└── Platforms/
    └── iOS/
        ├── AppDelegate.cs
        ├── WebViewHandler.cs
        ├── NavigationDelegate.cs
        └── DownloadDelegate.cs
Resources/
├── GoogleService-Info.plist   (gitignored — add manually)
Platforms/iOS/
└── Info.plist
```
