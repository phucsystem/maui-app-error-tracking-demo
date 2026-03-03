using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MauiFirebaseMetrics.Services;
using Plugin.Firebase.Crashlytics;

#if IOS
using MauiFirebaseMetrics.Platforms.iOS;
using Plugin.Firebase.Core.Platforms.iOS;
#endif

namespace MauiFirebaseMetrics;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .RegisterFirebaseServices()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if IOS
                handlers.AddHandler<WebView, MetricWebViewHandler>();
#endif
            });

        builder.Services.AddSingleton<CrashService>();
        builder.Services.AddSingleton<PerformanceService>();
        builder.Services.AddSingleton<DownloadService>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static MauiAppBuilder RegisterFirebaseServices(this MauiAppBuilder builder)
    {
        builder.ConfigureLifecycleEvents(events =>
        {
#if IOS
            events.AddiOS(iOS => iOS.WillFinishLaunching((_, __) =>
            {
                CrossFirebase.Initialize();
                return false;
            }));
#endif
        });

        builder.Services.AddSingleton(_ => CrossFirebaseCrashlytics.Current);
        return builder;
    }
}
