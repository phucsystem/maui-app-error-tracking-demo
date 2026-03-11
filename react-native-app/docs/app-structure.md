# React Native App Structure

## Directory Layout

```
react-native-app/
├── App.tsx                          # Root component: WebView + crash buttons + message handler
├── index.js                         # Entry point (registers App component)
├── app.json                         # App name & display config
├── package.json                     # Dependencies & scripts
├── tsconfig.json                    # TypeScript configuration
├── metro.config.js                  # Metro bundler config
├── babel.config.js                  # Babel transpiler config
├── firebase.json                    # Firebase feature toggles (Crashlytics, Analytics, Perf)
├── jest.config.js                   # Jest test configuration
├── Gemfile                          # Ruby dependencies (CocoaPods)
│
├── services/
│   ├── crash-service.ts             # Firebase Crashlytics wrapper
│   ├── performance-service.ts       # Firebase Performance + Navigation Timing
│   └── download-service.ts          # Download progress & throughput tracking (prepared, unused)
│
├── ios/
│   ├── Podfile                      # CocoaPods: Firebase Core, Crashlytics, Analytics, Performance
│   └── ErrorTrackingDemo/
│       ├── AppDelegate.swift        # FirebaseApp.configure() on launch
│       ├── Info.plist               # iOS app config
│       └── GoogleService-Info.plist # Firebase credentials (not committed)
│
├── android/
│   ├── app/
│   │   ├── build.gradle             # Android build config (Firebase auto-linked)
│   │   └── src/main/java/.../
│   │       ├── MainActivity.kt      # Standard ReactActivity
│   │       └── MainApplication.kt   # ReactHost initialization
│   └── build.gradle                 # Root Android build config
│
└── __tests__/
    └── App.test.tsx                  # Basic render test
```

## Component Architecture

```
App (SafeAreaProvider)
└── AppContent (SafeAreaView)
    ├── WebView
    │   ├── source: GitHub Pages demo site
    │   ├── injectedJavaScript: CONSOLE_OVERRIDE_SCRIPT (pre-load)
    │   ├── onLoadEnd → inject IMAGE_OBSERVER + NAV_TIMING scripts (post-load)
    │   ├── onMessage → handleMessage() → route by level
    │   ├── onError → handleError() → Crashlytics non-fatal
    │   └── onShouldStartLoadWithRequest → start perf trace
    │
    └── ButtonRow
        ├── [Native Crash] → crashService.triggerNativeCrash()
        └── [Non-Fatal]    → crashService.recordNonFatal()
```

## Service Layer

### CrashService (`services/crash-service.ts`)
Thin wrapper around `@react-native-firebase/crashlytics`.

| Method | Purpose |
|--------|---------|
| `recordNonFatal(error, context)` | Report handled exception with context attribute |
| `setMetadata(key, value)` | Set Crashlytics custom key-value |
| `log(message)` | Append to Crashlytics log buffer |
| `setUserId(userId)` | Associate session with user |
| `triggerNativeCrash()` | Force fatal crash (testing only) |

Guard pattern: constructor sets `isEnabled` flag; all methods no-op if Crashlytics unavailable.

### PerformanceService (`services/performance-service.ts`)
Manages Firebase Performance traces and Crashlytics metadata for timing data.

| Method | Purpose |
|--------|---------|
| `recordAppStartup()` | Log time from module load to first render |
| `recordInitialWebViewLoad(ms)` | Track initial WebView load + total from app start |
| `recordPageLoad(url, ms)` | Store page load wall-clock time |
| `startWebViewTrace(url)` | Begin Firebase Performance trace |
| `recordNavigationTiming(url, data)` | Record TTFB, DOM interactive/complete, load event |
| `recordHeavyImageLoad(url, ms, kb)` | Track slow/large images (>500ms or >200KB) |
| `stopActiveTrace()` | Stop current Firebase trace |

### DownloadService (`services/download-service.ts`)
Tracks download progress and throughput. **Prepared but not wired to UI** — ready for future download tracking features.

## Key Dependencies

| Package | Purpose |
|---------|---------|
| `react-native-webview` | Embedded web content + JS bridge |
| `@react-native-firebase/app` | Firebase core |
| `@react-native-firebase/crashlytics` | Crash & error reporting |
| `@react-native-firebase/perf` | Performance monitoring traces |
| `react-native-safe-area-context` | Safe area insets for notch devices |

## App Lifecycle

```
1. Module load        → appStartTime captured
2. AppContent mount   → recordAppStartup()
3. WebView created    → CONSOLE_OVERRIDE_SCRIPT injected (before page load)
4. Navigation starts  → startWebViewTrace(), record timestamp
5. Page loads         → recordPageLoad(), inject IMAGE_OBSERVER + NAV_TIMING scripts
6. Messages arrive    → handleMessage() routes by level to CrashService or PerformanceService
7. User taps buttons  → trigger native crash or non-fatal error
```
