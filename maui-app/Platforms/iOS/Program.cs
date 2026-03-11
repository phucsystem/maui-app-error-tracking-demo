using UIKit;

namespace MauiFirebaseMetrics;

internal static class AppStartup
{
    internal static readonly DateTime ProcessStartTime = DateTime.UtcNow;
}

public class Program
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
