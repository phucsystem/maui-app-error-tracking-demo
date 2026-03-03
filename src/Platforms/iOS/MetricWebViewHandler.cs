using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class MetricWebViewHandler : WebViewHandler
{
    private const string ConsoleHandlerName = "nativeConsole";
    private static readonly string ConsoleOverrideScript = """
        (function() {
            var orig = { log: console.log, warn: console.warn, error: console.error };
            function send(level, args) {
                try {
                    var msg = Array.prototype.map.call(args, function(a) {
                        return typeof a === 'object' ? JSON.stringify(a) : String(a);
                    }).join(' ');
                    window.webkit.messageHandlers.nativeConsole.postMessage(
                        JSON.stringify({ level: level, message: msg })
                    );
                } catch(e) {}
            }
            console.log = function() { send('log', arguments); orig.log.apply(console, arguments); };
            console.warn = function() { send('warn', arguments); orig.warn.apply(console, arguments); };
            console.error = function() { send('error', arguments); orig.error.apply(console, arguments); };
            window.addEventListener('error', function(e) {
                send('error', [e.message + ' at ' + e.filename + ':' + e.lineno + ':' + e.colno]);
            });
            window.addEventListener('unhandledrejection', function(e) {
                send('error', ['Unhandled promise rejection: ' + e.reason]);
            });
        })();
        """;

    private NavigationDelegate? _navigationDelegate;
    private ConsoleLogHandler? _consoleLogHandler;

    protected override WKWebView CreatePlatformView()
    {
        var config = new WKWebViewConfiguration();
        config.AllowsInlineMediaPlayback = true;
        config.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;

        return new WKWebView(CGRect.Empty, config);
    }

    protected override void ConnectHandler(WKWebView platformView)
    {
        base.ConnectHandler(platformView);

        var crashService = MauiContext!.Services.GetRequiredService<CrashService>();
        var perfService = MauiContext!.Services.GetRequiredService<PerformanceService>();
        var downloadService = MauiContext!.Services.GetRequiredService<DownloadService>();

        // Console log bridge
        _consoleLogHandler = new ConsoleLogHandler(crashService);
        var userContent = platformView.Configuration.UserContentController;
        userContent.AddScriptMessageHandler(_consoleLogHandler, ConsoleHandlerName);
        userContent.AddUserScript(new WKUserScript(
            new NSString(ConsoleOverrideScript),
            WKUserScriptInjectionTime.AtDocumentStart,
            isForMainFrameOnly: false));

        _navigationDelegate = new NavigationDelegate(crashService, perfService, downloadService);
        _navigationDelegate.NavigationCompleted += OnNavigationCompleted;
        platformView.NavigationDelegate = _navigationDelegate;

        // Re-trigger URL load since MAUI's delegate handled the initial one
        if (VirtualView is Microsoft.Maui.Controls.WebView webView
            && webView.Source is UrlWebViewSource urlSource
            && !string.IsNullOrEmpty(urlSource.Url))
        {
            platformView.LoadRequest(new NSUrlRequest(new NSUrl(urlSource.Url)));
        }
    }

    private void OnNavigationCompleted()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (VirtualView is Microsoft.Maui.Controls.WebView webView)
            {
                Element? current = webView;
                while (current is not null)
                {
                    if (current is MainPage mainPage)
                    {
                        mainPage.HideLoadingIndicator();
                        return;
                    }
                    current = current.Parent;
                }
            }
        });
    }

    protected override void DisconnectHandler(WKWebView platformView)
    {
        if (_navigationDelegate is not null)
            _navigationDelegate.NavigationCompleted -= OnNavigationCompleted;

        platformView.Configuration.UserContentController.RemoveScriptMessageHandler(ConsoleHandlerName);
        platformView.Configuration.UserContentController.RemoveAllUserScripts();

        platformView.NavigationDelegate = null!;
        _navigationDelegate = null;
        _consoleLogHandler = null;
        base.DisconnectHandler(platformView);
    }
}
