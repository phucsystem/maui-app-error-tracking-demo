---
phase: 3
status: done
priority: high
effort: 15min
depends_on: []
---

# Phase 3: React Native — Message Handler Update

## Context

- Project: `/Users/phuc/Code/05-demo/maui-app-error-tracking-demo/react-native-app`
- File: `App.tsx`
- Current `handleMessage` routes: `nav-timing`, `perf`, `error`, and generic log/warn

## Files to Modify

### `App.tsx`

**Change 1: Simplify CONSOLE_OVERRIDE_SCRIPT**

Remove `window.error` and `unhandledrejection` listeners — web app bridge now owns error reporting. Keep console.log/warn/error forwarding for breadcrumb logging.

```typescript
// Before (lines 33-38):
  window.addEventListener('error', function(e) {
    send('error', [e.message + ' at ' + e.filename + ':' + e.lineno + ':' + e.colno]);
  });
  window.addEventListener('unhandledrejection', function(e) {
    send('error', ['Unhandled promise rejection: ' + e.reason]);
  });

// After: DELETE these 6 lines entirely.
// Web app's firebase-bridge.ts handles these now.
```

**Change 2: Add new message level routing in handleMessage**

After the existing `payload.level === 'error'` block, add handlers for 5 new levels:

```typescript
// HTTP error (4xx/5xx)
if (payload.level === 'http-error') {
  const errorData = payload.data || {};
  crashService.setMetadata('http_error_status', String(errorData.status || 'unknown'));
  crashService.setMetadata('http_error_url', String(errorData.url || 'unknown').substring(0, 128));
  crashService.setMetadata('http_error_method', String(errorData.method || 'unknown'));
  crashService.recordNonFatal(
    new Error(`HTTP ${errorData.status}: ${payload.message}`),
    'WebView.httpError',
  );
  return;
}

// Network error
if (payload.level === 'network-error') {
  const errorData = payload.data || {};
  crashService.setMetadata('network_error_url', String(errorData.url || 'unknown').substring(0, 128));
  crashService.recordNonFatal(
    new Error(`Network error: ${payload.message}`),
    'WebView.networkError',
  );
  return;
}

// Slow response (>1s)
if (payload.level === 'slow-response') {
  const metricData = payload.data || {};
  crashService.setMetadata('slow_response_url', String(metricData.url || 'unknown').substring(0, 128));
  crashService.setMetadata('slow_response_ms', String(metricData.durationMs || 0));
  crashService.recordNonFatal(
    new Error(`Slow response: ${payload.message}`),
    'WebView.slowResponse',
  );
  return;
}

// Image load failure
if (payload.level === 'image-error') {
  const errorData = payload.data || {};
  crashService.setMetadata('image_error_url', String(errorData.url || 'unknown').substring(0, 128));
  crashService.recordNonFatal(
    new Error(`Image failed: ${payload.message}`),
    'WebView.imageError',
  );
  return;
}

// Long task (slow DOM operation)
if (payload.level === 'slow-task') {
  const metricData = payload.data || {};
  crashService.setMetadata('slow_task_ms', String(metricData.durationMs || 0));
  crashService.recordNonFatal(
    new Error(`Long task: ${payload.message}`),
    'WebView.slowTask',
  );
  return;
}
```

**Change 3: Update payload parsing**

Current `handleMessage` parses `event.nativeEvent.data` and checks for `payload.level` and `payload.message`. The new bridge events also include `payload.data` (optional object). The existing parsing handles this — no change needed to the JSON.parse logic.

However, the `data` field needs to be accessible. Current code destructures `level` and `message` implicitly. Just access `payload.data` directly — it's already parsed.

## Todo

- [x] Remove `window.addEventListener('error', ...)` from CONSOLE_OVERRIDE_SCRIPT (2 listeners, 6 lines)
- [x] Add `http-error` handler in handleMessage
- [x] Add `network-error` handler in handleMessage
- [x] Add `slow-response` handler in handleMessage
- [x] Add `image-error` handler in handleMessage
- [x] Add `slow-task` handler in handleMessage
- [x] Verify TypeScript compiles: `npx tsc --noEmit`

## Success Criteria

- New bridge event levels are routed to `crashService.recordNonFatal()`
- Each error type sets relevant Crashlytics metadata
- Error context string identifies source (WebView.httpError, WebView.networkError, etc.)
- Existing levels (nav-timing, perf, error, log, warn) still work unchanged
- Console.log/warn forwarding preserved in CONSOLE_OVERRIDE_SCRIPT
