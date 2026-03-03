using CoreGraphics;
using Microsoft.Maui.Handlers;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class MetricWebViewHandler : WebViewHandler
{
    private NavigationDelegate? _navigationDelegate;

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

        _navigationDelegate = new NavigationDelegate(crashService, perfService, downloadService);
        platformView.NavigationDelegate = _navigationDelegate;
    }

    protected override void DisconnectHandler(WKWebView platformView)
    {
        platformView.NavigationDelegate = null!;
        _navigationDelegate = null;
        base.DisconnectHandler(platformView);
    }
}
