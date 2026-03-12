# ErrorTrackingDemo — React Native WebView Error & Performance Tracker

React Native iOS app with Firebase Crashlytics integration for tracking:

- **Crash errors** — Fatal crashes via `crashlytics().crash()`
- **Non-fatal errors** — Handled exceptions reported via `CrashService`
- **WebView errors** — Navigation failures captured via WebView `onError`
- **JS console bridge** — Captures `console.log/warn/error` from WebView content
- **WebView error bridge** — Structured error reporting from web app (HTTP, network, image, slow responses/tasks)
- **API load time** — All WebView API calls tracked as Firebase Performance HTTP metrics
- **Performance** — App startup, WebView page load, Navigation Timing V2 with Firebase Performance traces
- **Heavy images** — PerformanceObserver detects slow/large image loads
- **Device context** — User UUID, country ISO, brand, device model attached to all traces and Crashlytics metadata

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

        AXIOS["axios-client.ts<br/>Request interceptor (timing)<br/>Response interceptor (errors + slow + api-timing)"]
        BRIDGE["firebase-bridge.ts<br/>reportError() · reportMetric() · reportApiTiming()<br/>initBridge() · MutationObserver · PerformanceObserver"]
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

        subgraph ERRORS["Error Handlers"]
            direction LR
            HTTP["http-error<br/>HTTP 4xx/5xx"]
            NETWORK["network-error<br/>Connection failures"]
            SLOW["slow-response<br/>Response >1s"]
            IMG["image-error<br/>Broken images"]
            TASK["slow-task<br/>Long task >100ms"]
            ERR["error<br/>console.error"]
        end

        subgraph PERFHANDLERS["Performance Handlers"]
            direction LR
            APITIMING["api-timing<br/>All API call metrics"]
            PERF["perf<br/>Heavy image data"]
            NAV["nav-timing<br/>Navigation Timing V2"]
        end

        DEVCTX["deviceContextService<br/>device-context-service.ts<br/>UUID · country · brand · model"]
        CRASH["crashService<br/>crash-service.ts<br/>setMetadata() + recordNonFatal()"]
        PERFSERV["performanceService<br/>performance-service.ts<br/>Firebase Performance traces + HTTP metrics"]

        HANDLER --> ROUTER
        ROUTER --> ERRORS
        ROUTER --> PERFHANDLERS
        HTTP & NETWORK & SLOW & IMG & TASK & ERR --> CRASH
        APITIMING & PERF & NAV --> PERFSERV
        DEVCTX -.-> CRASH
        DEVCTX -.-> PERFSERV
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
    style ERRORS fill:#ffebee,stroke:#c62828,stroke-width:1px
    style PERFHANDLERS fill:#e8f5e9,stroke:#2e7d32,stroke-width:1px
    style DEVCTX fill:#e3f2fd,stroke:#1565c0,stroke-width:2px
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

## Firebase Metrics: Standard vs Custom

| Product | Type | Metric | Description |
|---------|------|--------|-------------|
| Crashlytics | Standard | Fatal crashes | Stack traces, device info, OS version |
| Crashlytics | Standard | Crash-free users % | Auto-calculated from crash data |
| Crashlytics | Standard | Breadcrumb timeline | Auto-collected app lifecycle events |
| Crashlytics | **Custom** | 25 metadata keys | `setAttribute()` — startup_ms, http_error_status, js_ttfb_ms, user_uuid, country_iso, brand, device_model, etc. |
| Crashlytics | **Custom** | 7 non-fatal error types | `recordError()` — JS errors, HTTP, network, slow response, image, slow task, WebView nav |
| Crashlytics | **Custom** | Breadcrumb logs | `log()` — "WebView load complete", "Nav timing [...]", etc. |
| Performance | Standard | App start time | Auto-collected app startup duration |
| Performance | Standard | HTTP/S network requests | Auto-collected latency, payload size, success rate |
| Performance | Standard | Screen rendering | Slow/frozen frames (Android only) |
| Performance | **Custom** | `webview_page_load` trace | 1 trace with 4 metrics + 5 attributes: `ttfb_ms`, `dom_interactive_ms`, `dom_complete_ms`, `total_load_ms`; attrs: `url`, `user_uuid`, `country_iso`, `brand`, `device_model` |
| Performance | **Custom** | WebView API HTTP metrics | `newHttpMetric()` per API call — url, method, status, duration, payload size + device context attrs |
| Analytics | Standard | Session & engagement | `first_open`, `session_start`, `screen_view`, retention |
| Analytics | **Custom** | *(none)* | Auto-collection only — no custom events needed for this demo |

## Message Protocol

The web app sends structured JSON via `postMessage()`:

```json
{
  "level": "http-error | network-error | slow-response | image-error | slow-task | api-timing",
  "message": "Human-readable description",
  "data": {
    "url": "https://...",
    "status": 500,
    "method": "GET",
    "durationMs": 1234,
    "responseSize": 2048
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

## Documentation

- [Error & Log Handling from WebView](react-native-app/docs/error-and-log-handling-from-webview.md) — JS bridge architecture, message protocol, routing, and Crashlytics metadata reference

## Project Structure

```
react-native-app/
├── App.tsx                     # Main screen: WebView + crash buttons + message handler
├── index.js                    # Entry point
├── services/
│   ├── crash-service.ts        # Firebase Crashlytics wrapper (log, recordNonFatal, metadata)
│   ├── performance-service.ts  # Startup, page load, Nav Timing V2, Firebase Perf traces
│   ├── device-context-service.ts # Device context: user UUID, country ISO, brand, device model
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
| `user_uuid` | Persistent random UUID per device (first launch) |
| `country_iso` | Country ISO from device locale (e.g., "US", "VN") |
| `brand` | Commercial brand (e.g., "Brand A", "Brand B") |
| `device_model` | Device model (e.g., "iPhone 15 Pro") |

## References

- [Demo WebView error source code](https://github.com/phucsystem/demo-web-view-error) — Web app loaded in the WebView for error and performance testing
- [Set up alerts for performance issues (Firebase)](https://firebase.google.com/docs/perf-mon/alerts) — Configure alerts for metric regressions in Firebase Performance Monitoring
- [App start, foreground, background traces (Firebase)](https://firebase.google.com/docs/perf-mon/app-start-foreground-background-traces?platform=ios) — Auto-collected app lifecycle performance traces on iOS
- [Screen rendering performance traces (Firebase)](https://firebase.google.com/docs/perf-mon/screen-traces?platform=ios) — Monitor slow and frozen frames per screen on iOS
