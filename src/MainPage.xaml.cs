using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics;

public partial class MainPage : ContentPage
{
    private readonly PerformanceService _performanceService;

    public MainPage(PerformanceService performanceService)
    {
        _performanceService = performanceService;
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

    private void OnTriggerCrashClicked(object? sender, EventArgs args)
    {
        throw new Exception("Test crash triggered by user");
    }
}
