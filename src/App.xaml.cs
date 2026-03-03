using MauiFirebaseMetrics.Services;

namespace MauiFirebaseMetrics;

public partial class App : Application
{
    private readonly CrashService _crashService;

    public App(CrashService crashService)
    {
        _crashService = crashService;
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            _crashService.Log($"Unhandled: {exception.Message}");

            if (!args.IsTerminating)
            {
                _crashService.RecordNonFatal(exception, "AppDomain.UnhandledException");
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _crashService.RecordNonFatal(args.Exception, "TaskScheduler.UnobservedTaskException");
        args.SetObserved();
    }
}
