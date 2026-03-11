using System.Text.Json;
using Foundation;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class ConsoleLogHandler : NSObject, IWKScriptMessageHandler
{
    private readonly CrashService _crashService;
    private readonly PerformanceService? _performanceService;

    public ConsoleLogHandler(CrashService crashService, PerformanceService? performanceService = null)
    {
        _crashService = crashService;
        _performanceService = performanceService;
    }

    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
    {
        if (message.Body is not NSString jsonString)
            return;

        try
        {
            var payload = JsonSerializer.Deserialize<ConsolePayload>(
                jsonString.ToString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (payload is null) return;

            var logLine = $"[JS {payload.Level}] {payload.Message}";
            Console.WriteLine(logLine);
            _crashService.Log(logLine);

            if (payload.Level == "perf")
            {
                HandlePerfMessage(payload.Message);
                return;
            }

            if (payload.Level == "error")
            {
                _crashService.SetMetadata("last_js_error", Truncate(payload.Message, 128));
                _crashService.RecordNonFatal(
                    new Exception($"JS error: {payload.Message}"),
                    context: "console.error");
            }
        }
        catch (JsonException)
        {
            // Malformed payload — ignore
        }
    }

    private void HandlePerfMessage(string message)
    {
        try
        {
            var imageData = JsonSerializer.Deserialize<HeavyImagePayload>(
                message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (imageData is null || imageData.Type != "heavy_image") return;

            _performanceService?.RecordHeavyImageLoad(
                imageData.Url, imageData.DurationMs, imageData.SizeKb);
        }
        catch (JsonException)
        {
            // Malformed perf payload — ignore
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    private sealed class ConsolePayload
    {
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private sealed class HeavyImagePayload
    {
        public string Type { get; set; } = "";
        public string Url { get; set; } = "";
        public long DurationMs { get; set; }
        public long SizeKb { get; set; }
        public long DecodedBodySize { get; set; }
    }
}
