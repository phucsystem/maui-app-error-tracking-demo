---
phase: 1
status: done
priority: high
effort: 30min
---

# Phase 1: Web App — Firebase Bridge Module

## Context

- Project: `/Users/phuc/Code/05-demo/demo-web-view-error`
- Framework: Vite + TypeScript (ES2022, strict mode, `verbatimModuleSyntax: true`)
- Currently: No bridge, no postMessage, no Firebase. Pure DOM + console logging.

## Files to Create

### `src/firebase-bridge.ts`

Bridge module that detects `ReactNativeWebView` and sends structured events.

**Core API:**

```typescript
// Detection
const isInWebView = !!window.ReactNativeWebView;

// Send structured event to RN
function sendToNative(level: string, message: string, data?: Record<string, unknown>): void

// Public API
export function reportError(level: string, message: string, data?: Record<string, unknown>): void
export function reportMetric(level: string, message: string, data?: Record<string, unknown>): void
export function initBridge(): void
```

**Implementation steps:**

1. **`sendToNative()`** — checks `window.ReactNativeWebView`, calls `postMessage(JSON.stringify({level, message, data}))`, no-op in browser
2. **`reportError()`** — wraps sendToNative, also logs to console.error for browser debugging
3. **`reportMetric()`** — wraps sendToNative, also logs to console.info for browser debugging
4. **`initBridge()`** — called once on app startup, sets up:
   - Image error observer (see below)
   - Long task observer (see below)

**Image error observer (inside initBridge):**

```typescript
// Watch for new <img> elements and attach onerror
const imageObserver = new MutationObserver((mutations) => {
  for (const mutation of mutations) {
    for (const node of mutation.addedNodes) {
      if (node instanceof HTMLImageElement) {
        attachImageErrorHandler(node);
      }
      // Also check children of added nodes
      if (node instanceof HTMLElement) {
        node.querySelectorAll('img').forEach(attachImageErrorHandler);
      }
    }
  }
});

imageObserver.observe(document.body, { childList: true, subtree: true });

// Also attach to existing images
document.querySelectorAll('img').forEach(attachImageErrorHandler);
```

`attachImageErrorHandler(img)`:
- Skip if already observed (`img.dataset.bridgeObserved`)
- Set `img.dataset.bridgeObserved = 'true'`
- `img.addEventListener('error', () => reportError('image-error', ...))`

**Long task observer (inside initBridge):**

```typescript
try {
  const longTaskObserver = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      if (entry.duration > 100) {
        reportMetric('slow-task', `Long task detected: ${Math.round(entry.duration)}ms`, {
          durationMs: Math.round(entry.duration),
          startTime: Math.round(entry.startTime),
        });
      }
    }
  });
  longTaskObserver.observe({ type: 'longtask', buffered: true });
} catch {
  // longtask not supported (WKWebView) — silent no-op
}
```

**TypeScript type for ReactNativeWebView:**

```typescript
declare global {
  interface Window {
    ReactNativeWebView?: {
      postMessage(message: string): void;
    };
  }
}
```

### `src/axios-client.ts`

See Phase 2.

## Todo

- [x] Create `src/firebase-bridge.ts` with sendToNative, reportError, reportMetric
- [x] Add Window type augmentation for ReactNativeWebView
- [x] Add MutationObserver for image error detection
- [x] Add PerformanceObserver for long tasks (with try/catch fallback)
- [x] Export `initBridge()` for main.ts to call

## Success Criteria

- `reportError('http-error', ...)` sends postMessage when in WebView
- `reportError('http-error', ...)` logs to console when in browser
- Image errors on dynamically created `<img>` elements trigger `image-error` events
- Long tasks >100ms trigger `slow-task` events (Chrome/Android only, no-op on iOS)
- No errors when running in regular browser (bridge detects and no-ops)
