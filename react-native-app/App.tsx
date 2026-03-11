import React, {useCallback, useEffect, useRef, useState} from 'react';
import {
  ActivityIndicator,
  Alert,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import {SafeAreaProvider, SafeAreaView} from 'react-native-safe-area-context';
import {WebView, type WebViewMessageEvent, type WebViewNavigation} from 'react-native-webview';
import {crashService} from './services/crash-service';
import {performanceService} from './services/performance-service';

const WEBVIEW_URL = 'https://phucsystem.github.io/demo-web-view-error/';

const CONSOLE_OVERRIDE_SCRIPT = `
(function() {
  var orig = { log: console.log, warn: console.warn, error: console.error };
  function send(level, args) {
    try {
      var msg = Array.prototype.map.call(args, function(a) {
        return typeof a === 'object' ? JSON.stringify(a) : String(a);
      }).join(' ');
      window.ReactNativeWebView.postMessage(
        JSON.stringify({ level: level, message: msg })
      );
    } catch(e) {}
  }
  console.log = function() { send('log', arguments); orig.log.apply(console, arguments); };
  console.warn = function() { send('warn', arguments); orig.warn.apply(console, arguments); };
  console.error = function() { send('error', arguments); orig.error.apply(console, arguments); };
})();
true;
`;

const IMAGE_OBSERVER_SCRIPT = `
(function() {
  if (window.__imageObserverInstalled) return;
  window.__imageObserverInstalled = true;

  var observer = new PerformanceObserver(function(list) {
    list.getEntries().forEach(function(entry) {
      if (entry.initiatorType !== 'img') return;
      var durationMs = Math.round(entry.duration);
      var sizeKb = Math.round((entry.transferSize || 0) / 1024);
      if (durationMs > 500 || sizeKb > 200) {
        window.ReactNativeWebView.postMessage(
          JSON.stringify({
            level: 'perf',
            message: JSON.stringify({
              type: 'heavy_image',
              url: entry.name,
              durationMs: durationMs,
              sizeKb: sizeKb,
              decodedBodySize: entry.decodedBodySize || 0
            })
          })
        );
      }
    });
  });
  observer.observe({ type: 'resource', buffered: true });
})();
true;
`;

const NAV_TIMING_V2_SCRIPT = `
(function() {
  var entries = performance.getEntriesByType('navigation');
  if (!entries || entries.length === 0) return;
  var nav = entries[0];
  var result = JSON.stringify({
    ttfb: Math.round(nav.responseStart - nav.requestStart),
    domInteractive: Math.round(nav.domInteractive),
    domComplete: Math.round(nav.domComplete),
    loadEventEnd: Math.round(nav.loadEventEnd)
  });
  window.ReactNativeWebView.postMessage(
    JSON.stringify({ level: 'nav-timing', message: result })
  );
})();
true;
`;

function handlePerfMessage(message: string): void {
  try {
    const imageData = JSON.parse(message);
    if (!imageData || imageData.type !== 'heavy_image') return;

    performanceService.recordHeavyImageLoad(
      imageData.url,
      imageData.durationMs,
      imageData.sizeKb,
    );
  } catch {
    // Malformed perf payload — ignore
  }
}

function AppContent(): React.JSX.Element {
  const webViewRef = useRef<WebView>(null);
  const [loading, setLoading] = useState(true);
  const navigationStartTime = useRef<number>(Date.now());
  const isInitialLoad = useRef(true);

  useEffect(() => {
    performanceService.recordAppStartup();
  }, []);

  const handleLoadEnd = useCallback(() => {
    const wallClockMs = Date.now() - navigationStartTime.current;
    setLoading(false);

    crashService.log(`WebView load complete (${Math.round(wallClockMs)}ms wall-clock)`);
    performanceService.recordPageLoad(WEBVIEW_URL, wallClockMs);

    if (isInitialLoad.current) {
      isInitialLoad.current = false;
      performanceService.recordInitialWebViewLoad(wallClockMs);
    }

    webViewRef.current?.injectJavaScript(IMAGE_OBSERVER_SCRIPT);
    webViewRef.current?.injectJavaScript(NAV_TIMING_V2_SCRIPT);
  }, []);

  const handleError = useCallback(
    (syntheticEvent: {nativeEvent: {code: number; description: string; url: string}}) => {
      const {code, description, url} = syntheticEvent.nativeEvent;
      if (code === -999) return;

      setLoading(false);
      performanceService.stopActiveTrace();
      crashService.setMetadata('webview_error_code', String(code));
      crashService.setMetadata('webview_url', url || 'unknown');
      crashService.recordNonFatal(
        new Error(`WebView nav failed [${code}]: ${description} — ${url}`),
        'WebView.onError',
      );
    },
    [],
  );

  const handleMessage = useCallback((event: WebViewMessageEvent) => {
    try {
      const payload = JSON.parse(event.nativeEvent.data);
      if (!payload?.level || !payload?.message) return;

      const logLine = `[JS ${payload.level}] ${payload.message}`;
      console.log(logLine);
      crashService.log(logLine);

      if (payload.level === 'nav-timing') {
        try {
          const timingData = JSON.parse(payload.message);
          performanceService.recordNavigationTiming(WEBVIEW_URL, timingData);
        } catch {
          crashService.log('Failed to parse nav-timing payload');
        }
        return;
      }

      if (payload.level === 'perf') {
        handlePerfMessage(payload.message);
        return;
      }

      if (payload.level === 'error') {
        const truncatedMsg =
          payload.message.length > 128
            ? payload.message.substring(0, 128)
            : payload.message;
        crashService.setMetadata('last_js_error', truncatedMsg);
        crashService.recordNonFatal(
          new Error(`JS error: ${payload.message}`),
          'console.error',
        );
        return;
      }

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

      if (payload.level === 'network-error') {
        const errorData = payload.data || {};
        crashService.setMetadata('network_error_url', String(errorData.url || 'unknown').substring(0, 128));
        crashService.recordNonFatal(
          new Error(`Network error: ${payload.message}`),
          'WebView.networkError',
        );
        return;
      }

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

      if (payload.level === 'image-error') {
        const errorData = payload.data || {};
        crashService.setMetadata('image_error_url', String(errorData.url || 'unknown').substring(0, 128));
        crashService.recordNonFatal(
          new Error(`Image failed: ${payload.message}`),
          'WebView.imageError',
        );
        return;
      }

      if (payload.level === 'slow-task') {
        const metricData = payload.data || {};
        crashService.setMetadata('slow_task_ms', String(metricData.durationMs || 0));
        crashService.recordNonFatal(
          new Error(`Long task: ${payload.message}`),
          'WebView.slowTask',
        );
        return;
      }
    } catch {
      // Malformed payload — ignore
    }
  }, []);

  const handleNativeCrash = useCallback(() => {
    crashService.log('User triggered native crash');
    crashService.triggerNativeCrash();
  }, []);

  const handleNonFatal = useCallback(() => {
    crashService.recordNonFatal(
      new Error('Test non-fatal triggered by user'),
      'MainScreen.OnNonFatalPressed',
    );
    Alert.alert('Non-Fatal Sent', 'Non-fatal error recorded to Crashlytics.');
  }, []);

  const handleShouldStartLoad = useCallback((event: WebViewNavigation) => {
    navigationStartTime.current = Date.now();
    crashService.log('WebView navigation started');
    performanceService.startWebViewTrace(event.url || WEBVIEW_URL);
    return true;
  }, []);

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.webViewContainer}>
        <WebView
          ref={webViewRef}
          source={{uri: WEBVIEW_URL}}
          style={styles.webView}
          injectedJavaScript={CONSOLE_OVERRIDE_SCRIPT}
          onLoadEnd={handleLoadEnd}
          onError={handleError}
          onMessage={handleMessage}
          onShouldStartLoadWithRequest={handleShouldStartLoad}
          javaScriptEnabled
          allowsInlineMediaPlayback
          mediaPlaybackRequiresUserAction={false}
          cacheEnabled={false}
        />
        {loading && (
          <ActivityIndicator size="large" style={styles.loadingIndicator} />
        )}
      </View>

      <View style={styles.buttonRow}>
        <TouchableOpacity
          style={[styles.button, styles.crashButton]}
          onPress={handleNativeCrash}>
          <Text style={styles.buttonText}>Native Crash</Text>
        </TouchableOpacity>
        <TouchableOpacity
          style={[styles.button, styles.nonFatalButton]}
          onPress={handleNonFatal}>
          <Text style={styles.buttonText}>Non-Fatal</Text>
        </TouchableOpacity>
      </View>
    </SafeAreaView>
  );
}

function App(): React.JSX.Element {
  return (
    <SafeAreaProvider>
      <AppContent />
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#fff',
  },
  webViewContainer: {
    flex: 1,
  },
  webView: {
    flex: 1,
  },
  loadingIndicator: {
    position: 'absolute',
    top: '50%',
    left: '50%',
    marginLeft: -18,
    marginTop: -18,
  },
  buttonRow: {
    flexDirection: 'row',
    padding: 10,
    gap: 8,
  },
  button: {
    flex: 1,
    paddingVertical: 12,
    borderRadius: 6,
    alignItems: 'center',
  },
  crashButton: {
    backgroundColor: '#FF3B30',
  },
  nonFatalButton: {
    backgroundColor: '#FF9500',
  },
  buttonText: {
    color: '#fff',
    fontWeight: '600',
    fontSize: 16,
  },
});

export default App;
