---
status: in-progress
created: 2026-03-11
type: feature
complexity: medium
mode: fast
---

# WebView → React Native Error Bridge

## Summary

Build a structured error bridge between the web app (demo-web-view-error) and the React Native app so all non-fatal errors, HTTP failures, image errors, and slow operations are captured and forwarded to Firebase Crashlytics.

## Phases

| # | Phase | Status | Files | Effort |
|---|-------|--------|-------|--------|
| 1 | [Web App: Firebase Bridge Module](phase-01-web-app-firebase-bridge.md) | done | 2 new files | ~30 min |
| 2 | [Web App: Axios Integration](phase-02-web-app-axios-integration.md) | done | 2 files modified | ~20 min |
| 3 | [React Native: Message Handler](phase-03-rn-message-handler.md) | done | 1 file modified | ~15 min |
| 4 | [Build & Deploy](phase-04-build-deploy.md) | pending | n/a | ~10 min |

## Dependencies

- Phase 2 depends on Phase 1 (axios-client imports firebase-bridge)
- Phase 3 is independent of Phase 1-2 (RN side, different project)
- Phase 4 depends on all prior phases

## Key Decisions

- Web app owns error reporting (bridge built into web app, not injected from RN)
- Axios interceptors for HTTP error capture (not monkey-patching fetch)
- CONSOLE_OVERRIDE_SCRIPT simplified — remove error/rejection handlers, keep console.log/warn forwarding
- Message protocol: `{level, message, data?}` JSON via `window.ReactNativeWebView.postMessage()`
- Slow response threshold: >1s
- Long task threshold: >100ms

## Projects

| Project | Path |
|---------|------|
| Web app | `/Users/phuc/Code/05-demo/demo-web-view-error` |
| React Native app | `/Users/phuc/Code/05-demo/maui-app-error-tracking-demo/react-native-app` |
