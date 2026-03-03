using Plugin.Firebase.Crashlytics;

namespace MauiFirebaseMetrics.Services;

public class CrashService
{
    private readonly IFirebaseCrashlytics? _crashlytics;
    private readonly bool _isEnabled;

    public CrashService()
    {
        try
        {
            _crashlytics = CrossFirebaseCrashlytics.Current;
            _crashlytics.SetCrashlyticsCollectionEnabled(true);
            _isEnabled = true;
        }
        catch (Exception ex)
        {
            _isEnabled = false;
            System.Diagnostics.Debug.WriteLine($"Crashlytics unavailable: {ex.Message}");
        }
    }

    public void RecordNonFatal(Exception exception, string? context = null)
    {
        if (!_isEnabled) return;

        if (context is not null)
        {
            _crashlytics!.SetCustomKey("error_context", context);
        }

        _crashlytics!.RecordException(exception);
    }

    public void SetMetadata(string key, string value)
    {
        if (!_isEnabled) return;
        _crashlytics!.SetCustomKey(key, value);
    }

    public void Log(string message)
    {
        if (!_isEnabled) return;
        _crashlytics!.Log(message);
    }

    public void SetUserId(string userId)
    {
        if (!_isEnabled) return;
        _crashlytics!.SetUserId(userId);
    }
}
