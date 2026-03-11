# Brainstorm: WebView → React Native Error Bridge

**Date:** 2026-03-11
**Status:** Agreed

---

## Problem Statement

Current error capture from WebView relies on injected `CONSOLE_OVERRIDE_SCRIPT` which intercepts `console.error`, `window.error`, and `unhandledrejection`. This misses:
- HTTP 4xx/5xx responses (fetch resolves, doesn't throw)
- Failed image loads (DOM onerror, not console)
- Network failures (partial — only if logged to console.error)
- Slow DOM operations (perf degradation, no error)

Goal: web app explicitly reports all non-fatal errors + slow responses via structured bridge to React Native, which forwards to Firebase Crashlytics.

## Architecture

```
demo-web-view-error (Vite + TS)              React Native App
┌────────────────────────────────┐           ┌───────────────────────────┐
│                                │           │                           │
│  firebase-bridge.ts            │  postMsg  │  handleMessage()          │
│  ├─ detectNativeBridge()       │ ───────>  │  ├─ 'http-error'         │
│  ├─ reportError(type, data)    │           │  │   → recordNonFatal()  │
│  └─ reportMetric(type, data)   │           │  ├─ 'network-error'      │
│                                │           │  │   → recordNonFatal()  │
│  axios-client.ts               │           │  ├─ 'slow-response'      │
│  ├─ response interceptor       │           │  │   → recordNonFatal()  │
│  │   (4xx/5xx → reportError)   │           │  ├─ 'image-error'        │
│  │   (>1s → reportMetric)      │           │  │   → recordNonFatal()  │
│  └─ error interceptor          │           │  ├─ 'slow-task'          │
│      (network fail → report)   │           │  │   → recordNonFatal()  │
│                                │           │  └─ existing levels...    │
│  Image error observer          │           │      (nav-timing, perf,  │
│  └─ MutationObserver + onerror │           │       log, warn, error)  │
│                                │           │                           │
│  Long task observer            │           │                           │
│  └─ PerformanceObserver        │           │                           │
│      (longtask entries)        │           │                           │
└────────────────────────────────┘           └───────────────────────────┘
```

## Message Protocol

All messages use existing `{level, message, data?}` format.

### HTTP Error (4xx/5xx)
```json
{
  "level": "http-error",
  "message": "POST /api/users → 500 Internal Server Error",
  "data": {
    "url": "https://api.example.com/users",
    "method": "POST",
    "status": 500,
    "statusText": "Internal Server Error",
    "durationMs": 234,
    "responseBody": "{\"error\":\"null reference\"}"
  }
}
```

### Network Error
```json
{
  "level": "network-error",
  "message": "GET /api/data → Network Error",
  "data": {
    "url": "https://api.example.com/data",
    "method": "GET",
    "errorMessage": "Failed to fetch"
  }
}
```

### Slow Response (>1s)
```json
{
  "level": "slow-response",
  "message": "GET /api/photos → 200 OK (2340ms)",
  "data": {
    "url": "https://api.example.com/photos",
    "method": "GET",
    "status": 200,
    "durationMs": 2340
  }
}
```

### Image Error
```json
{
  "level": "image-error",
  "message": "Image failed: https://example.com/photo.jpg",
  "data": {
    "url": "https://example.com/photo.jpg",
    "alt": "Photo description"
  }
}
```

### Slow Task (Long DOM operation)
```json
{
  "level": "slow-task",
  "message": "Long task detected: 350ms",
  "data": {
    "durationMs": 350,
    "startTime": 12345.67
  }
}
```

## Implementation Plan

### Phase 1: Web App (demo-web-view-error)

#### 1A. Create `src/firebase-bridge.ts`
- Detect `window.ReactNativeWebView` presence
- Export `reportError(level, message, data?)` — sends structured postMessage
- Export `reportMetric(level, message, data?)` — same format
- No-op gracefully when not in WebView (browser dev mode)

#### 1B. Create `src/axios-client.ts`
- Install `axios` dependency
- Create shared instance with:
  - **Response interceptor:** 4xx/5xx → `reportError('http-error', ...)`
  - **Response interceptor:** >1s duration → `reportMetric('slow-response', ...)`
  - **Error interceptor:** network failures → `reportError('network-error', ...)`
- Track request start time via interceptor for duration calculation

#### 1C. Add image error observer in `src/firebase-bridge.ts`
- MutationObserver watches for new `<img>` elements
- Attach `onerror` handler to each
- On error → `reportError('image-error', ...)`
- Also observe existing images on init

#### 1D. Add long task observer in `src/firebase-bridge.ts`
- PerformanceObserver for `longtask` entry type
- Filter tasks >100ms (default longtask threshold is 50ms, use 100ms to reduce noise)
- On detection → `reportMetric('slow-task', ...)`

#### 1E. Update `src/main.ts`
- Replace all `fetch()` calls with axios
- Import and initialize bridge on app startup
- Remove manual console.error logging where bridge now handles it

### Phase 2: React Native App

#### 2A. Update `App.tsx` handleMessage()
- Add routing for new levels: `http-error`, `network-error`, `slow-response`, `image-error`, `slow-task`
- Each calls `crashService.recordNonFatal()` with contextual metadata

#### 2B. Remove redundant CONSOLE_OVERRIDE_SCRIPT error interception
- Keep console.log/warn forwarding for debugging
- Remove window.error and unhandledrejection handlers (web app bridge handles these now)
- Keep as fallback? Or fully delegate to web app bridge?

**Decision:** Remove error interception from injected script. Web app owns error reporting. Keep console.log/warn for breadcrumb logging.

#### 2C. Update `services/crash-service.ts` (if needed)
- May need to set metadata for new error types (http_error_status, network_error_url, etc.)
- Or keep generic — recordNonFatal already accepts context string

### Phase 3: Verify
- Build web app, deploy to GitHub Pages
- Build RN app to device
- Trigger each error scenario
- Verify events appear in Firebase Crashlytics as non-fatal errors

## Files to Modify

### Web App (demo-web-view-error)
| File | Action | Purpose |
|------|--------|---------|
| `package.json` | Modify | Add `axios` dependency |
| `src/firebase-bridge.ts` | Create | Bridge module + image/task observers |
| `src/axios-client.ts` | Create | Shared axios instance with interceptors |
| `src/main.ts` | Modify | Replace fetch → axios, init bridge |

### React Native App
| File | Action | Purpose |
|------|--------|---------|
| `App.tsx` | Modify | Add message routing for new levels, simplify injected script |

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Axios adds bundle size to web app | ~13KB gzipped, acceptable for demo |
| Long task observer not supported in WKWebView | Graceful no-op with try/catch, still works in Chrome WebView |
| Bridge messages flood RN during "Random API Chaos" (10 rapid errors) | Already truncating to 128 chars, Crashlytics deduplicates similar errors |
| Web app deployed to GitHub Pages before RN is updated | New message levels ignored by old RN (no-op, no crash) |

## Success Criteria

- [ ] HTTP 4xx/5xx from "Random API Chaos" appear as non-fatal errors in Crashlytics
- [ ] Failed images from "Heavy Images" appear as non-fatal errors
- [ ] Slow responses (>1s) from "Heavy API Request" reported
- [ ] Network failures (offline test) captured
- [ ] Long DOM tasks from "Slow DOM Load" detected
- [ ] No duplicate events (bridge + console override not both reporting same error)
- [ ] Web app works normally in browser without WebView (bridge no-ops)
