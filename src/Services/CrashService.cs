using Plugin.Firebase.Crashlytics;

namespace MauiFirebaseMetrics.Services;

public class CrashService
{
    private readonly IFirebaseCrashlytics _crashlytics;

    public CrashService()
    {
        _crashlytics = CrossFirebaseCrashlytics.Current;
        _crashlytics.SetCrashlyticsCollectionEnabled(true);
    }

    public void RecordNonFatal(Exception exception, string? context = null)
    {
        if (context is not null)
        {
            _crashlytics.SetCustomKey("error_context", context);
        }

        _crashlytics.RecordException(exception);
    }

    public void SetMetadata(string key, string value)
    {
        _crashlytics.SetCustomKey(key, value);
    }

    public void Log(string message)
    {
        _crashlytics.Log(message);
    }

    public void SetUserId(string userId)
    {
        _crashlytics.SetUserId(userId);
    }
}
