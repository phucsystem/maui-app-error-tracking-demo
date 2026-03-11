# Code Review: WebView Error Bridge

**Date:** 2026-03-11
**Reviewer:** code-reviewer
**Scope:** 4 files across 2 projects (web app + React Native app)

## Scope

- Files reviewed:
  - `demo-web-view-error/src/firebase-bridge.ts` (new, 77 LOC)
  - `demo-web-view-error/src/axios-client.ts` (new, 67 LOC)
  - `demo-web-view-error/src/main.ts` (modified, 338 LOC)
  - `react-native-app/App.tsx` (modified, 347 LOC)
- Focus: correctness, security, edge cases, duplicate event prevention

## Overall Assessment

Solid implementation. Message protocol is clean, bridge gracefully no-ops in browsers, Axios interceptors correctly separate HTTP vs network errors, and RN handlers cover all 5 new levels. A few issues worth addressing before shipping.

## Critical Issues

### 1. Duplicate Crashlytics recordNonFatal for bridge errors that also call console.error

**File:** `firebase-bridge.ts` line 24, `App.tsx` lines 168-179 and 181-190

`reportError()` calls `console.error()` after `sendToNative()`. The RN-injected `CONSOLE_OVERRIDE_SCRIPT` intercepts `console.error` and sends a `{level: "error", message: ...}` postMessage. Meanwhile, `sendToNative()` already sent the structured `{level: "http-error", ...}` message. Both arrive at `handleMessage`:

1. The structured message (e.g. `http-error`) triggers `crashService.recordNonFatal()` at line 186.
2. The console.error intercept sends `{level: "error", message: "[bridge:http-error] ..."}` which triggers another `crashService.recordNonFatal()` at line 174.

**Impact:** Every `reportError()` call produces 2 Crashlytics non-fatal events for the same incident. This doubles error volume in Firebase console, making triage harder and potentially inflating error counts.

**Fix options:**
- (A) In `reportError()`, use `console.warn()` instead of `console.error()` so the console override sends level `warn` instead of `error`, which does not trigger recordNonFatal.
- (B) In the RN `handleMessage`, when `payload.level === 'error'`, check if the message starts with `[bridge:` and skip recordNonFatal for bridge-originated console.error messages.
- (C) Remove `console.error()` from `reportError()` entirely and rely solely on the structured bridge message.

**Recommendation:** Option (B) is safest — preserves logging while preventing duplicates.

### 2. Metadata key collision / overwrite race

**File:** `App.tsx` — all new handlers

Crashlytics `setAttribute` is a global key-value store, not scoped per event. If two errors fire in quick succession (e.g., network-error then http-error), the metadata from the second overwrites the first before `recordError` for the first has been processed. Keys like `http_error_url`, `network_error_url` etc. are distinct per type so cross-type collision is mitigated. However, two rapid http-errors would overwrite each other's metadata.

**Impact:** Medium. Metadata on second error is correct; metadata on first error may reflect second error's values depending on timing. This is an inherent Crashlytics limitation, not a code bug per se.

**Mitigation:** Accept as known limitation. Could batch metadata into the Error message string itself for critical fields (status, url) to guarantee they're captured per-event.

## High Priority

### 3. isInWebView evaluated once at module load — breaks hot reload / lazy load scenarios

**File:** `firebase-bridge.ts` line 9

```typescript
const isInWebView = !!window.ReactNativeWebView;
```

This is evaluated at module parse time. If `firebase-bridge.ts` is loaded before `CONSOLE_OVERRIDE_SCRIPT` or `ReactNativeWebView` is injected, it will be `false` permanently. In WKWebView, the `ReactNativeWebView` object is injected before any page JS runs, so in production this is fine. But during development with HMR or if the web app is loaded outside a WebView first then opened in one, the bridge silently fails.

**Impact:** Low in production, medium in development/testing.

**Fix:** Evaluate lazily:
```typescript
function isInWebView(): boolean {
  return !!window.ReactNativeWebView;
}
```

### 4. No payload size guard on postMessage

**File:** `firebase-bridge.ts` line 14

`JSON.stringify({ level, message, data })` — if `data` contains large objects (e.g., a full response body), the serialized string could be very large. `postMessage` on iOS WKWebView has practical limits around 128MB but serialization cost of large payloads can cause jank.

**Impact:** Low. The axios-client already truncates responseBody to 512 chars. But `reportError` / `reportMetric` accept arbitrary `data` from caller, so future callers could pass large objects.

**Fix:** Truncate the serialized message to a reasonable limit (e.g., 4KB) in `sendToNative()`.

### 5. startWebViewTrace returns a Promise but handleShouldStartLoad is synchronous

**File:** `App.tsx` line 254

```typescript
performanceService.startWebViewTrace(event.url || WEBVIEW_URL);
```

`startWebViewTrace` is `async` — the returned promise is not awaited. If the trace creation is slow, subsequent events could race with it. The `onShouldStartLoadWithRequest` callback must return synchronously (`return true`), so awaiting is not possible here.

**Impact:** Low. The trace is created in the background; worst case, early metrics are lost for that navigation. This is pre-existing behavior, not introduced by this change.

## Medium Priority

### 6. Random API Chaos slow-response threshold inconsistency

**File:** `main.ts` lines 322-329

The random API chaos simulation checks `scenario.delay > 1000` to decide whether to report a slow-response. But `scenario.delay` is the predefined timeout value, not the actual measured duration. The actual `duration` (measured via `performance.now()`) could differ from `scenario.delay` due to setTimeout imprecision. Should use `duration > 1000` for consistency with the axios interceptor threshold.

**Fix:**
```typescript
if (duration > 1000) {  // instead of scenario.delay > 1000
```

### 7. MutationObserver does not catch images created via `new Image()` that are never appended to DOM

**File:** `firebase-bridge.ts` lines 44-58, `main.ts` line 79

The image error observer watches DOM mutations. In `main.ts`, images are created via `new Image()` (line 79) and only appended to DOM on success (line 86). If the image fails to load (onerror), the image element is never appended. The MutationObserver never sees it, so the `attachImageErrorHandler` from the bridge is never attached.

However, `main.ts` already has its own `onerror` handler at line 102 that logs to the UI. The bridge's image-error detection is redundant here. For images dynamically created and never added to DOM, the bridge's MutationObserver approach has a blind spot.

**Impact:** Low for this demo. The `onerror` handler in main.ts logs locally but does not call `reportError('image-error', ...)`, so those failures are NOT reported to Crashlytics via the bridge. Only images appended to the DOM that subsequently fail are caught by the bridge.

**Fix:** Add `reportError('image-error', ...)` in the `onerror` handler of `loadHeavyImages()` in main.ts, or register the bridge listener before appending.

### 8. renderApiResult XSS via title parameter

**File:** `main.ts` lines 121-128

```typescript
apiResult.innerHTML = `...<strong>${title}</strong>...`
```

The `title` parameter is interpolated directly into innerHTML. In the current code, titles are hardcoded strings, so no actual vulnerability. But if title ever includes user/server-controlled data, this is an XSS vector. The `jsonString` in the `<pre>` tag is also unescaped.

**Impact:** Low (no user input flows into title currently). Worth noting for future-proofing.

### 9. Plan TODO list incomplete

**File:** `phase-03-rn-message-handler.md`

The plan says to remove `window.addEventListener('error', ...)` from CONSOLE_OVERRIDE_SCRIPT. Looking at the current `App.tsx` CONSOLE_OVERRIDE_SCRIPT (lines 17-35), those listeners are already absent. Either they were removed as part of this work (good), or the plan was based on an older version. The TODO checkbox should be marked done.

All 5 new handler TODOs are implemented in the code.

## Low Priority

### 10. Long task observer threshold (100ms) is very aggressive

**File:** `firebase-bridge.ts` line 64

The PerformanceObserver for `longtask` fires for entries >50ms by spec. The code further filters to >100ms. In practice, on mobile WebViews many legitimate operations (layout, paint) produce 100-200ms long tasks. This could generate a high volume of `slow-task` reports to Crashlytics.

**Impact:** Noise in Crashlytics. Consider raising threshold to 200-300ms, or rate-limiting reports.

### 11. var usage in injected scripts

**File:** `App.tsx` lines 19, 42, 70

The injected JavaScript strings use `var` instead of `let`/`const`. This is intentional for compatibility (injected into arbitrary WebView context), but worth noting per the codebase rules against `var`. These are string-injected scripts, not TypeScript source, so the rule is less applicable.

## Positive Observations

- Clean separation: bridge module in web app, message routing in RN app
- Graceful no-op in browser via `isInWebView` check
- Good defensive coding: `|| {}` fallbacks, `.substring(0, 128)` truncation
- Axios module augmentation for `metadata` is properly typed
- `try/catch` around all bridge operations prevents cascading failures
- `data-bridge-observed` attribute prevents duplicate image listeners
- Error interceptor correctly distinguishes HTTP errors (has response) from network errors (no response)
- responseBody truncated to 512 chars in axios interceptor

## Recommended Actions (Priority Order)

1. **[CRITICAL]** Fix duplicate Crashlytics events — implement option (B): skip recordNonFatal for console.error messages prefixed with `[bridge:`
2. **[MEDIUM]** Use measured `duration` not `scenario.delay` for slow-response threshold in Random API Chaos
3. **[MEDIUM]** Add `reportError('image-error', ...)` in loadHeavyImages onerror for bridge coverage
4. **[LOW]** Consider making `isInWebView` a function for lazy evaluation
5. **[LOW]** Raise long-task threshold or add rate limiting
6. **[LOW]** Mark plan TODO checkboxes as complete

## Metrics

- Type Coverage: Good (TypeScript throughout, module augmentation for axios)
- Test Coverage: N/A (no tests in scope)
- Linting Issues: Not checked (per project rules)

## Unresolved Questions

1. Is the long task PerformanceObserver (`longtask` type) actually supported in WKWebView? The code handles the exception gracefully, but if it never works on iOS, the slow-task handler in RN is dead code for iOS users.
2. Should `slow-response` and `slow-task` be recorded as non-fatal errors or as performance metrics only? Currently they go through `recordNonFatal` which treats them as errors in Crashlytics rather than performance data.
