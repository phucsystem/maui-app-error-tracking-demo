using Foundation;
using MauiFirebaseMetrics.Services;
using WebKit;

namespace MauiFirebaseMetrics.Platforms.iOS;

public class DownloadDelegate : NSObject, IWKDownloadDelegate
{
    private readonly DownloadService _downloadService;
    private string _downloadId = string.Empty;
    private string _sourceUrl = string.Empty;

    public DownloadDelegate(DownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    [Export("download:decideDestinationUsingResponse:suggestedFilename:completionHandler:")]
    public void DecideDestination(
        WKDownload download,
        NSUrlResponse response,
        string suggestedFilename,
        Action<NSUrl> completionHandler)
    {
        _downloadId = Guid.NewGuid().ToString("N");
        _sourceUrl = response.Url?.AbsoluteString ?? "unknown";

        var expectedBytes = response.ExpectedContentLength > 0
            ? (long?)response.ExpectedContentLength
            : null;

        _downloadService.OnDownloadStarted(_downloadId, _sourceUrl, expectedBytes);

        var tempDir = Path.GetTempPath();
        var destinationPath = Path.Combine(tempDir, suggestedFilename);
        completionHandler(NSUrl.FromFilename(destinationPath));
    }

    [Export("download:didReceiveData:totalBytesWritten:totalBytesExpectedToWrite:")]
    public void DidWriteData(
        WKDownload download,
        long bytesWritten,
        long totalBytesWritten,
        long totalBytesExpectedToWrite)
    {
        _downloadService.OnDownloadProgress(_downloadId, totalBytesWritten, totalBytesExpectedToWrite);
    }

    [Export("downloadDidFinish:")]
    public void DidFinish(WKDownload download)
    {
        _downloadService.OnDownloadCompleted(_downloadId, localPath: string.Empty);
    }

    [Export("download:didFailWithError:resumeData:")]
    public void DidFail(WKDownload download, NSError error, NSData? resumeData)
    {
        var exception = new Exception(
            $"Download failed [{error.Code}]: {error.LocalizedDescription} — {_sourceUrl}");
        _downloadService.OnDownloadFailed(_downloadId, exception);
    }
}
