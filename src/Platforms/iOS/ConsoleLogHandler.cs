using System.Text.Json;
using Foundation;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class ConsoleLogHandler : NSObject, IWKScriptMessageHandler
{
    private readonly CrashService _crashService;

    public ConsoleLogHandler(CrashService crashService)
    {
        _crashService = crashService;
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

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    private sealed class ConsolePayload
    {
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
