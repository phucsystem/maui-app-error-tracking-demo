---
phase: 4
status: pending
priority: medium
effort: 10min
depends_on: [1, 2, 3]
---

# Phase 4: Build & Deploy

## Steps

### 1. Build & deploy web app

```bash
cd /Users/phuc/Code/05-demo/demo-web-view-error
npm run build
# Commit and push to trigger GitHub Pages deployment
git add -A
git commit -m "feat: add firebase bridge for RN WebView error reporting"
git push
```

Wait for GitHub Actions to deploy (~1-2 min).

### 2. Build RN app to device

```bash
cd /Users/phuc/Code/05-demo/maui-app-error-tracking-demo/react-native-app
# Fix HERMES_CLI_PATH after any pod install (known issue)
npx react-native run-ios --mode Debug --device
```

**Remember:** Patch `HERMES_CLI_PATH` in Pods xcconfig files if pods were reinstalled.

### 3. Verify each error scenario

| Button | Expected Bridge Event | Verify in Crashlytics |
|--------|----------------------|----------------------|
| Error Log | `error` (console.error forwarding) | Non-fatal: "JS error: ..." |
| Load Heavy Images | `image-error` (if any fail) | Non-fatal: "Image failed: ..." |
| Light API Request | (none — fast, successful) | No event |
| Heavy API Request | `slow-response` (>1s) | Non-fatal: "Slow response: ..." |
| Slow DOM Load | `slow-task` (longtask >100ms) | Non-fatal: "Long task: ..." (Android only) |
| Random API Chaos | `http-error` (60% fail rate) | Non-fatal: "HTTP 4xx/5xx: ..." |
| Random API Chaos | `slow-response` (slow success >1s) | Non-fatal: "Slow response: ..." |

### 4. Verify no duplicates

- Console.error from web app should NOT trigger both `error` (from console override) AND `http-error` (from bridge)
- The bridge calls `reportError()` which does NOT call console.error for http-error/network-error/image-error/slow-task levels
- CONSOLE_OVERRIDE_SCRIPT still forwards console.error calls — but bridge uses different levels, so no overlap

## Todo

- [ ] `npm run build` succeeds for web app
- [ ] Commit and push web app to trigger deploy
- [ ] Build RN app to device
- [ ] Test each button scenario
- [ ] Verify non-fatal errors in Firebase Crashlytics console
- [ ] Confirm no duplicate events
