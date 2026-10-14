using Microsoft.Extensions.Logging;

namespace PersonalResourceWorkspace.Application.Diagnostics;

public static partial class DiagnosticLog
{
    [LoggerMessage(100, LogLevel.Information, "Application started. Executable={Executable}; Version={Version}; DataRoot={DataRoot}; Source={Source}; Database={Database}")]
    public static partial void Startup(ILogger logger, string executable, string version, string dataRoot, string source, string database);

    [LoggerMessage(101, LogLevel.Information, "Loaded {Count} resources from database")]
    public static partial void ResourcesLoaded(ILogger logger, int count);

    [LoggerMessage(102, LogLevel.Error, "Operation failed: {Operation}; ResourceId={ResourceId}")]
    public static partial void OperationFailed(ILogger logger, string operation, Guid? resourceId, Exception exception);

    [LoggerMessage(103, LogLevel.Information, "Application window closed")]
    public static partial void Closed(ILogger logger);

    [LoggerMessage(200, LogLevel.Debug, "Preview started. ResourceId={ResourceId}; Type={Type}; Mode={Mode}; Origin={Origin}; ForceRefresh={ForceRefresh}")]
    public static partial void PreviewStarted(ILogger logger, Guid resourceId, string type, string mode, string origin, bool forceRefresh);

    [LoggerMessage(201, LogLevel.Debug, "Preview cache. ResourceId={ResourceId}; Path={Path}; Exists={Exists}; FingerprintMatches={FingerprintMatches}")]
    public static partial void PreviewCache(ILogger logger, Guid resourceId, string? path, bool exists, bool fingerprintMatches);

    [LoggerMessage(202, LogLevel.Debug, "Preview resolved. ResourceId={ResourceId}; Kind={Kind}; Origin={Origin}; Path={Path}; ElapsedMs={ElapsedMs}")]
    public static partial void PreviewResolved(ILogger logger, Guid resourceId, string kind, string origin, string? path, long elapsedMs);

    [LoggerMessage(203, LogLevel.Warning, "Preview fallback. ResourceId={ResourceId}; Reason={Reason}")]
    public static partial void PreviewFallback(ILogger logger, Guid resourceId, string reason, Exception? exception = null);

    [LoggerMessage(204, LogLevel.Debug, "Preview HTTP response. Stage={Stage}; Url={Url}; Status={Status}; ContentType={ContentType}; ContentLength={ContentLength}")]
    public static partial void HttpResponse(ILogger logger, string stage, string url, int status, string? contentType, long? contentLength);

    [LoggerMessage(205, LogLevel.Warning, "Preview candidate rejected. Stage={Stage}; Url={Url}; Reason={Reason}")]
    public static partial void CandidateRejected(ILogger logger, string stage, string url, string reason, Exception? exception = null);

    [LoggerMessage(206, LogLevel.Debug, "Preview metadata. Host={Host}; CoverFound={CoverFound}; IconFound={IconFound}")]
    public static partial void Metadata(ILogger logger, string host, bool coverFound, bool iconFound);

    [LoggerMessage(207, LogLevel.Debug, "Preview displayed. ResourceId={ResourceId}; ImageLoaded={ImageLoaded}; DescriptorChanged={DescriptorChanged}")]
    public static partial void PreviewDisplayed(ILogger logger, Guid resourceId, bool imageLoaded, bool descriptorChanged);
}
