using Foundation;
using MauiFirebaseMetrics.Services;
using UIKit;

namespace MauiFirebaseMetrics;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);

        var perfService = IPlatformApplication.Current?.Services
            .GetService<PerformanceService>();
        perfService?.RecordAppStartup();

        return result;
    }
}
