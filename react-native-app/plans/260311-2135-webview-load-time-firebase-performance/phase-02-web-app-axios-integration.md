---
phase: 2
status: done
priority: high
effort: 20min
depends_on: [1]
---

# Phase 2: Web App — Axios Integration

## Context

- Project: `/Users/phuc/Code/05-demo/demo-web-view-error`
- Currently uses `fetch()` in 2 places (light API, heavy API) + simulated API chaos (setTimeout, no real fetch)
- Random API Chaos uses `setTimeout` to simulate — NOT real fetch calls

## Key Insight

The "Random API Chaos" feature does NOT use `fetch()`. It simulates errors with `setTimeout`. So axios interceptors won't catch those. Two options:
1. **Convert chaos to real fetch/axios calls** — adds complexity, changes behavior
2. **Call reportError directly from chaos simulation** — simple, immediate

**Decision:** Call `reportError()` directly from the chaos simulation code. Only convert the 2 real fetch calls to axios. KISS.

## Files to Modify

### `package.json` — Add axios

```bash
cd /Users/phuc/Code/05-demo/demo-web-view-error
npm install axios
```

### `src/axios-client.ts` — Create shared instance

```typescript
import axios from 'axios';
import { reportError, reportMetric } from './firebase-bridge';

const apiClient = axios.create({
  timeout: 30000,
});

// Track request start time
apiClient.interceptors.request.use((config) => {
  config.metadata = { startTime: performance.now() };
  return config;
});

// Response interceptor: catch HTTP errors + slow responses
apiClient.interceptors.response.use(
  (response) => {
    const durationMs = Math.round(performance.now() - response.config.metadata.startTime);
    const method = (response.config.method || 'GET').toUpperCase();
    const url = response.config.url || 'unknown';

    // Slow response (>1s)
    if (durationMs > 1000) {
      reportMetric('slow-response', `${method} ${url} → ${response.status} OK (${durationMs}ms)`, {
        url,
        method,
        status: response.status,
        durationMs,
      });
    }

    return response;
  },
  (error) => {
    const config = error.config || {};
    const method = (config.method || 'GET').toUpperCase();
    const url = config.url || 'unknown';
    const startTime = config.metadata?.startTime || performance.now();
    const durationMs = Math.round(performance.now() - startTime);

    if (error.response) {
      // HTTP error (4xx/5xx)
      const { status, statusText, data } = error.response;
      const responseBody = typeof data === 'string' ? data : JSON.stringify(data);
      reportError('http-error', `${method} ${url} → ${status} ${statusText}`, {
        url,
        method,
        status,
        statusText,
        durationMs,
        responseBody: responseBody.substring(0, 512),
      });
    } else {
      // Network error
      reportError('network-error', `${method} ${url} → Network Error`, {
        url,
        method,
        errorMessage: error.message || 'Unknown network error',
      });
    }

    return Promise.reject(error);
  }
);

export { apiClient };
```

**Note:** Need to augment axios types for `metadata`:
```typescript
declare module 'axios' {
  interface InternalAxiosRequestConfig {
    metadata?: { startTime: number };
  }
}
```

### `src/main.ts` — Update

**Changes:**
1. Add `import { initBridge, reportError } from './firebase-bridge'`
2. Add `import { apiClient } from './axios-client'`
3. Call `initBridge()` at top of file (after DOM setup)
4. Replace `fetch()` in light API handler → `apiClient.get()`
5. Replace `fetch()` in heavy API handler → `apiClient.get()`
6. Add `reportError()` call in Random API Chaos error path
7. Add `reportMetric('slow-response', ...)` in Random API Chaos success path for slow scenarios (>1s)

**Light API change:**
```typescript
// Before:
const response = await fetch('https://jsonplaceholder.typicode.com/posts/1')
const data = await response.json()

// After:
const response = await apiClient.get('https://jsonplaceholder.typicode.com/posts/1')
const data = response.data
```

**Heavy API change:**
```typescript
// Before:
const response = await fetch('https://jsonplaceholder.typicode.com/photos')
const data = await response.json()

// After:
const response = await apiClient.get('https://jsonplaceholder.typicode.com/photos')
const data = response.data
```

**Random API Chaos — add bridge reporting:**
```typescript
// In the willFail branch, after setTimeout fires:
reportError('http-error', `Random API: ${scenario.status} ${scenario.statusText}`, {
  url: '/simulated/random-api',
  method: 'GET',
  status: scenario.status,
  statusText: scenario.statusText,
  durationMs: duration,
  responseBody: JSON.stringify(scenario.body),
});

// In success branch for slow scenarios (delay > 1000ms):
if (scenario.delay > 1000) {
  reportMetric('slow-response', `Random API: 200 OK (${duration}ms)`, {
    url: '/simulated/random-api',
    method: 'GET',
    status: 200,
    durationMs: duration,
  });
}
```

## Todo

- [x] Install axios: `npm install axios`
- [x] Create `src/axios-client.ts` with interceptors
- [x] Update `src/main.ts`: import bridge + axios, call `initBridge()`
- [x] Replace `fetch()` calls with `apiClient.get()`
- [x] Add `reportError()` to Random API Chaos error path
- [x] Add `reportMetric()` to Random API Chaos slow success path
- [x] Verify `npm run build` compiles without errors

## Success Criteria

- Light API & Heavy API requests go through axios interceptors
- HTTP errors from real APIs trigger `http-error` bridge events
- Slow real API responses (>1s) trigger `slow-response` bridge events
- Random API Chaos errors trigger `http-error` bridge events via direct call
- Slow simulated successes trigger `slow-response` bridge events
- App works identically in browser (bridge no-ops)
