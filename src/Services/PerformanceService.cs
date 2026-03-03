using System.Text.Json;

namespace MauiFirebaseMetrics.Services;

public class PerformanceService
{
    private readonly CrashService _crashService;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _lastPageLoadMs = new();

    public PerformanceService(CrashService crashService)
    {
        _crashService = crashService;
    }

    public void RecordAppStartup()
    {
        var startupMs = (DateTime.UtcNow - AppStartup.ProcessStartTime).TotalMilliseconds;
        _crashService.Log($"App startup: {startupMs:F0}ms");
        _crashService.SetMetadata("startup_ms", ((long)startupMs).ToString());
    }

    public void RecordPageLoad(string url, double wallClockMs)
    {
        _lastPageLoadMs[url] = wallClockMs;
        _crashService.SetMetadata("last_page_load_url", TruncateUrl(url));
        _crashService.SetMetadata("last_page_load_ms", ((long)wallClockMs).ToString());
    }

    public void RecordJsTiming(string url, string timingJson)
    {
        try
        {
            var timing = JsonSerializer.Deserialize<Dictionary<string, long>>(timingJson);
            if (timing is null) return;

            if (timing.TryGetValue("domComplete", out var domComplete))
                _crashService.SetMetadata("js_dom_complete_ms", domComplete.ToString());

            if (timing.TryGetValue("domInteractive", out var domInteractive))
                _crashService.SetMetadata("js_dom_interactive_ms", domInteractive.ToString());

            if (timing.TryGetValue("loadEventEnd", out var loadEventEnd))
                _crashService.SetMetadata("js_load_event_ms", loadEventEnd.ToString());

            _crashService.Log(
                $"JS timing [{TruncateUrl(url)}]: " +
                $"interactive={domInteractive}ms, complete={domComplete}ms, load={loadEventEnd}ms");
        }
        catch (JsonException ex)
        {
            _crashService.Log($"JS timing parse failed: {ex.Message}");
        }
    }

    public void RecordCustomMetric(string metricName, double durationMs)
    {
        _crashService.SetMetadata(metricName, ((long)durationMs).ToString());
        _crashService.Log($"Metric [{metricName}]: {durationMs:F0}ms");
    }

    private static string TruncateUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var stripped = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
            return stripped.Length > 128 ? stripped[..128] : stripped;
        }
        catch
        {
            return url.Length > 128 ? url[..128] : url;
        }
    }
}
