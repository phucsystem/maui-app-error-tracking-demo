using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics;

public partial class MainPage : ContentPage
{
    private readonly PerformanceService _performanceService;

    public MainPage(PerformanceService performanceService)
    {
        _performanceService = performanceService;
        InitializeComponent();

        MainWebView.Navigating += OnWebViewNavigating;
        MainWebView.Navigated += OnWebViewNavigated;
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs args)
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs args)
    {
        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;
    }
}
