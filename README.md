# ErrorTrackingDemo — React Native WebView Error & Performance Tracker

React Native iOS app with Firebase Crashlytics integration for tracking:

- **Crash errors** — Fatal crashes via `crashlytics().crash()`
- **Non-fatal errors** — Handled exceptions reported via `CrashService`
- **WebView errors** — Navigation failures captured via WebView `onError`
- **JS console bridge** — Captures `console.log/warn/error` from WebView content
- **Performance** — App startup time, WebView page load timing, JS `performance.timing`
- **Heavy images** — PerformanceObserver detects slow/large image loads

> **Legacy MAUI version** is preserved in `maui-app/` for reference.

## Prerequisites

- Node.js >= 22
- Xcode 16+
- CocoaPods
- Firebase project with iOS app registered and Crashlytics enabled

## Setup

1. Clone this repository
2. Download `GoogleService-Info.plist` from Firebase Console
3. Place it at `src/ios/ErrorTrackingDemo/GoogleService-Info.plist`
4. Install and run:

```bash
cd src
npm install
cd ios && pod install && cd ..
npx react-native run-ios
```

## Architecture

```
src/
├── App.tsx                     # Main screen: WebView + crash buttons + JS bridge
├── index.js                    # Entry point
├── services/
│   ├── crash-service.ts        # Firebase Crashlytics wrapper (log, recordNonFatal, metadata)
│   ├── performance-service.ts  # Startup, page load, JS timing, heavy image tracking
│   └── download-service.ts     # Download progress & throughput tracking
└── ios/
    ├── Podfile                 # CocoaPods with Firebase
    └── ErrorTrackingDemo/
        ├── AppDelegate.swift   # FirebaseApp.configure()
        └── GoogleService-Info.plist.template
```

## Crashlytics Keys Reference

| Key | Description |
|-----|-------------|
| `startup_ms` | App startup duration |
| `last_page_load_url` | Most recent WebView URL |
| `last_page_load_ms` | Page load wall-clock time |
| `js_dom_complete_ms` | Browser DOM complete time |
| `js_dom_interactive_ms` | Browser DOM interactive time |
| `js_load_event_ms` | Browser load event time |
| `webview_error_code` | WebView error code |
| `heavy_image_url` | Slow/large image URL |
| `heavy_image_duration_ms` | Image load duration |
| `heavy_image_size_kb` | Image transfer size |
| `last_download_duration_ms` | Download duration |
| `last_download_throughput_kbps` | Download speed |
