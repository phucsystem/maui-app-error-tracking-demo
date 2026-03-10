using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics;

public partial class MainPage : ContentPage
{
    private readonly PerformanceService _performanceService;
    private readonly CrashService _crashService;

    public MainPage(PerformanceService performanceService, CrashService crashService)
    {
        _performanceService = performanceService;
        _crashService = crashService;
        InitializeComponent();
    }

    public void HideLoadingIndicator()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
        });
    }

    private void OnNativeCrashClicked(object? sender, EventArgs args)
    {
#if IOS
        _crashService.Log("User triggered native crash");
        ObjCRuntime.Runtime.GetNSObject(IntPtr.Zero)!.GetHashCode();
#else
        throw new Exception("Test crash triggered by user");
#endif
    }

    private void OnNonFatalClicked(object? sender, EventArgs args)
    {
        _crashService.RecordNonFatal(
            new Exception("Test non-fatal triggered by user"),
            context: "MainPage.OnNonFatalClicked");
        DisplayAlert("Non-Fatal Sent", "Non-fatal error recorded to Crashlytics.", "OK");
    }
}
