# MauiFirebaseMetrics — WebView Error & Performance Tracker

.NET MAUI iOS app with Firebase Crashlytics integration for tracking:

- **Crash errors** — Fatal crashes auto-captured by Firebase Crashlytics
- **Non-fatal errors** — Handled exceptions reported via `CrashService`
- **WebView errors** — Navigation failures captured by `WKNavigationDelegate`
- **Performance** — App startup time, WebView page load timing, JS `performance.timing`
- **Network/Downloads** — Heavy file download tracking via `WKDownloadDelegate`

## Prerequisites

- .NET 9 SDK
- Xcode 16+
- Firebase project with iOS app registered and Crashlytics enabled

## Setup

1. Clone this repository
2. Download `GoogleService-Info.plist` from Firebase Console
3. Place it at `src/Platforms/iOS/GoogleService-Info.plist`
4. Build and run:

```bash
cd src
dotnet restore
dotnet build -f net9.0-ios
```

## Architecture

```
src/
├── MauiProgram.cs              # DI, Firebase init, handler registration
├── App.xaml.cs                 # Unhandled exception wiring
├── MainPage.xaml/.cs           # WebView host page
├── Services/
│   ├── CrashService.cs         # Fatal/non-fatal error reporting
│   ├── PerformanceService.cs   # Startup, page load, JS timing
│   └── DownloadService.cs      # Download progress & throughput
└── Platforms/iOS/
    ├── AppDelegate.cs           # Firebase.Core.App.Configure()
    ├── Program.cs               # Startup timestamp
    ├── MetricWebViewHandler.cs  # Custom MAUI WebView handler
    ├── NavigationDelegate.cs    # WKNavigationDelegate (errors, timing, downloads)
    └── DownloadDelegate.cs      # WKDownloadDelegate (progress, completion)
```

## Crashlytics Keys Reference

| Key | Description |
|-----|-------------|
| `startup_ms` | App startup duration |
| `last_page_load_url` | Most recent WebView URL |
| `last_page_load_ms` | Page load wall-clock time |
| `js_dom_complete_ms` | Browser DOM complete time |
| `webview_error_code` | NSURLError code |
| `last_download_duration_ms` | Download duration |
| `last_download_throughput_kbps` | Download speed |
