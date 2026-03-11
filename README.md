# ErrorTrackingDemo — React Native WebView Error & Performance Tracker

React Native iOS app with Firebase Crashlytics integration for tracking:

- **Crash errors** — Fatal crashes via `crashlytics().crash()`
- **Non-fatal errors** — Handled exceptions reported via `CrashService`
- **WebView errors** — Navigation failures captured via WebView `onError`
- **JS console bridge** — Captures `console.log/warn/error` from WebView content
- **WebView error bridge** — Structured error reporting from web app (HTTP, network, image, slow responses/tasks)
- **Performance** — App startup, WebView page load, Navigation Timing V2 with Firebase Performance traces
- **Heavy images** — PerformanceObserver detects slow/large image loads

> **Legacy MAUI version** is preserved in `maui-app/` for reference.

## Architecture Diagram

```mermaid
flowchart TB
    subgraph WEB["Web App (Vite + TypeScript) — GitHub Pages"]
        direction TB
        MAIN["main.ts<br/>DOM setup · button handlers · initBridge()"]

        subgraph SOURCES["Error & Metric Sources"]
            direction LR
            LIGHT["Light API<br/>posts/1"]
            HEAVY["Heavy API<br/>5000 photos"]
            CHAOS["Random API Chaos<br/>60% fail rate"]
            SLOWDOM["Slow DOM Load<br/>200-500 nodes"]
            IMAGES["Heavy Images<br/>5 large files"]
            CONSOLEERR["console.error()<br/>manual trigger"]
        end

        AXIOS["axios-client.ts<br/>Request interceptor (timing)<br/>Response interceptor (errors + slow >1s)"]
        BRIDGE["firebase-bridge.ts<br/>reportError() · reportMetric() · initBridge()<br/>MutationObserver (images) · PerformanceObserver (longtask)"]
        SEND["sendToNative()<br/>window.ReactNativeWebView.postMessage(JSON)"]
        OVERRIDE["CONSOLE_OVERRIDE_SCRIPT<br/>Injected by RN · forwards console.log/warn/error"]

        MAIN --> SOURCES
        LIGHT & HEAVY --> AXIOS
        CHAOS -- "direct call" --> BRIDGE
        AXIOS -- "reportError / reportMetric" --> BRIDGE
        IMAGES -- "MutationObserver" --> BRIDGE
        SLOWDOM -- "PerformanceObserver" --> BRIDGE
        BRIDGE --> SEND
        CONSOLEERR -- "console.error()" --> OVERRIDE
        OVERRIDE -- "{level:'error'}" --> SEND
    end

    SEND == "postMessage(JSON)<br/>{level, message, data?}" ==> HANDLER

    subgraph RN["React Native App (iOS)"]
        direction TB
        HANDLER["handleMessage()<br/>App.tsx · JSON.parse + level routing"]
        ROUTER{{"Level Router"}}

        subgraph NEW["New Bridge Handlers"]
            direction LR
            HTTP["http-error<br/>HTTP 4xx/5xx"]
            NETWORK["network-error<br/>Connection failures"]
            SLOW["slow-response<br/>Response >1s"]
            IMG["image-error<br/>Broken images"]
            TASK["slow-task<br/>Long task >100ms"]
        end

        subgraph EXISTING["Existing Handlers"]
            direction LR
            ERR["error<br/>console.error"]
            PERF["perf<br/>Heavy image data"]
            NAV["nav-timing<br/>Navigation Timing V2"]
        end

        CRASH["crashService<br/>crash-service.ts<br/>setMetadata() + recordNonFatal()"]
        PERFSERV["performanceService<br/>performance-service.ts<br/>Firebase Performance traces"]

        HANDLER --> ROUTER
        ROUTER --> NEW
        ROUTER -.-> EXISTING
        HTTP & NETWORK & SLOW & IMG & TASK --> CRASH
        ERR --> CRASH
        PERF & NAV --> PERFSERV
    end

    subgraph FIREBASE["Firebase"]
        direction LR
        CRASHLYTICS["Crashlytics<br/>Non-fatal errors + metadata"]
        FIREPERF["Performance<br/>Traces + custom metrics"]
        CONSOLE["Firebase Console<br/>Dashboard · Alerts"]
    end

    CRASH --> CRASHLYTICS
    PERFSERV --> FIREPERF
    CRASHLYTICS & FIREPERF --> CONSOLE

    style WEB fill:#f0f4ff,stroke:#4361ee,stroke-width:2px
    style RN fill:#f0fff0,stroke:#2d6a4f,stroke-width:2px
    style FIREBASE fill:#fff3e0,stroke:#e65100,stroke-width:2px
    style SOURCES fill:#fff8e1,stroke:#f4845f,stroke-width:1px
    style NEW fill:#ffebee,stroke:#c62828,stroke-width:1px
    style EXISTING fill:#efebe9,stroke:#795548,stroke-width:1px,stroke-dasharray:5 5
    style SEND fill:#009688,color:#fff,stroke:#00796b,stroke-width:2px
    style HANDLER fill:#fff,stroke:#2d6a4f,stroke-width:2px
    style CRASH fill:#ffcdd2,stroke:#c62828,stroke-width:2px
    style PERFSERV fill:#c8e6c9,stroke:#2e7d32,stroke-width:2px
    style CRASHLYTICS fill:#ff6f00,color:#fff,stroke:#e65100,stroke-width:2px
    style FIREPERF fill:#ff6f00,color:#fff,stroke:#e65100,stroke-width:2px
    style CONSOLE fill:#bf360c,color:#fff,stroke:#8c1a02,stroke-width:2px
    style BRIDGE fill:#e0f2f1,stroke:#009688,stroke-width:2px
    style AXIOS fill:#e8eaf6,stroke:#5c6bc0,stroke-width:2px
    style OVERRIDE fill:#fff8e1,stroke:#ff8f00,stroke-width:1px
```

## Message Protocol

The web app sends structured JSON via `postMessage()`:

```json
{
  "level": "http-error | network-error | slow-response | image-error | slow-task",
  "message": "Human-readable description",
  "data": {
    "url": "https://...",
    "status": 500,
    "method": "GET",
    "durationMs": 1234
  }
}
```

## Prerequisites

- Node.js >= 22
- Xcode 16+
- CocoaPods
- Firebase project with iOS app registered and Crashlytics enabled

## Setup

1. Clone this repository
2. Download `GoogleService-Info.plist` from Firebase Console
3. Place it at `react-native-app/ios/ErrorTrackingDemo/GoogleService-Info.plist`
4. Install and run:

```bash
cd react-native-app
npm install
cd ios && pod install && cd ..
npx react-native run-ios
```

## Project Structure

```
react-native-app/
├── App.tsx                     # Main screen: WebView + crash buttons + message handler
├── index.js                    # Entry point
├── services/
│   ├── crash-service.ts        # Firebase Crashlytics wrapper (log, recordNonFatal, metadata)
│   ├── performance-service.ts  # Startup, page load, Nav Timing V2, Firebase Perf traces
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
| `js_ttfb_ms` | Time to first byte |
| `js_dom_interactive_ms` | Browser DOM interactive time |
| `js_dom_complete_ms` | Browser DOM complete time |
| `js_load_event_ms` | Browser load event time |
| `webview_error_code` | WebView error code |
| `http_error_status` | HTTP error status code |
| `http_error_url` | Failed HTTP request URL |
| `http_error_method` | HTTP method (GET/POST/etc.) |
| `network_error_url` | Network failure URL |
| `slow_response_url` | Slow response URL |
| `slow_response_ms` | Slow response duration |
| `image_error_url` | Failed image URL |
| `slow_task_ms` | Long task duration |
| `heavy_image_url` | Slow/large image URL |
| `heavy_image_duration_ms` | Image load duration |
| `heavy_image_size_kb` | Image transfer size |
