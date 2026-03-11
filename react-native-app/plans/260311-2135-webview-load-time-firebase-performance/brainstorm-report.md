# Brainstorm: WebView Load Time Tracking via Firebase Performance

**Date:** 2026-03-11
**Status:** Agreed
**Approach:** Firebase Performance custom traces with Navigation Timing v2

---

## Problem Statement

WebView load times currently stored as Crashlytics metadata — only visible on crash/error reports. Need standalone, real-time visibility into phased WebView loading performance in Firebase Console.

## Requirements

- **Phased breakdown:** TTFB, DOM interactive, DOM complete, full load
- **Real-time visibility:** Data in Firebase Console within minutes
- **Event counts & averages:** See how many loads, average durations
- **Filterable by URL**

## Evaluated Approaches

### 1. Firebase Analytics Only
- **Pros:** Already installed, simple (~20 lines)
- **Cons:** 24h reporting delay, no percentiles, not designed for timing
- **Verdict:** Rejected — 24h delay conflicts with real-time need

### 2. Firebase Performance Custom Traces ✅
- **Pros:** Real-time (~minutes), designed for timing, supports custom metrics per trace, distribution/percentiles
- **Cons:** New dependency, slightly more code (~40 lines)
- **Verdict:** Selected — best fit for real-time phased timing

### 3. Both Analytics + Performance
- **Verdict:** Rejected — overkill for demo app, violates KISS

## Recommended Solution

### Architecture

```
WebView (JS) ──Navigation Timing v2──> postMessage({level:'perf-trace', data:{...}})
                                              │
                                              ▼
                              React Native onMessage handler
                                              │
                                              ▼
                              Firebase Performance Custom Trace
                              ├─ metric: ttfb_ms
                              ├─ metric: dom_interactive_ms
                              ├─ metric: dom_complete_ms
                              ├─ metric: total_load_ms
                              └─ attribute: url
```

### Implementation Steps

#### 1. Install Firebase Performance
```bash
npm install @react-native-firebase/perf
cd ios && pod install
```

#### 2. Replace JS_TIMING_SCRIPT with Navigation Timing v2
Use `PerformanceNavigationTiming` API instead of legacy `performance.timing`:
```javascript
const entries = performance.getEntriesByType('navigation');
if (entries.length > 0) {
  const nav = entries[0];
  // nav.responseStart - nav.requestStart = TTFB
  // nav.domInteractive
  // nav.domComplete
  // nav.loadEventEnd
  // All relative to nav.startTime (0), no subtraction from navigationStart needed
}
```

**Why v2 over legacy:**
- Values are relative (no need to subtract `navigationStart`)
- More accurate (high-resolution timestamps)
- `performance.timing` is deprecated
- Well-supported: iOS WKWebView (Safari 15+), Android WebView (Chrome 57+)

#### 3. Create Performance Trace in performance-service.ts
```
- Start trace on navigation begin (onShouldStartLoadWithRequest)
- On JS timing message received:
  - putMetric('ttfb_ms', responseStart - requestStart)
  - putMetric('dom_interactive_ms', domInteractive)
  - putMetric('dom_complete_ms', domComplete)
  - putMetric('total_load_ms', loadEventEnd)
  - putAttribute('url', truncatedUrl)
- Stop trace
```

#### 4. Update App.tsx message handler
- Add `perf-trace` level to message routing
- Pass timing data to performance service trace methods

### Files to Modify
- `package.json` — add `@react-native-firebase/perf`
- `ios/Podfile` — add Firebase Performance pod (auto via RN Firebase)
- `services/performance-service.ts` — add trace start/stop/metric methods
- `App.tsx` — replace JS_TIMING_SCRIPT, update message handler

### Files to NOT Create
- No new files needed — extend existing service + component

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Navigation Timing v2 unavailable on old WebView | No timing data | Fallback to legacy `performance.timing` in JS script |
| Firebase Perf SDK adds app size | ~1-2MB | Acceptable for demo |
| Trace not stopped if WebView errors mid-load | Orphaned trace | Add timeout + error handler to stop trace on failure |
| Pod install regenerates wrong HERMES_CLI_PATH | Build failure | Re-patch xcconfig after pod install (known issue) |

## Success Criteria

- [ ] Firebase Console > Performance > Custom traces shows `webview_load` trace
- [ ] Trace metrics show TTFB, DOM interactive, DOM complete, total load
- [ ] Data appears within minutes of app usage
- [ ] Traces filterable by URL attribute
- [ ] No regression in existing Crashlytics error tracking

## Next Steps

1. Install `@react-native-firebase/perf`
2. Implement trace logic in `performance-service.ts`
3. Update JS bridge script to Navigation Timing v2
4. Update `App.tsx` message handler
5. Build to device, trigger loads, verify in Firebase Console
