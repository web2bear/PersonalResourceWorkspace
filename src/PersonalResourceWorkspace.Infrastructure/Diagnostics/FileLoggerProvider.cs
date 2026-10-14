using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PersonalResourceWorkspace.Infrastructure.Diagnostics;

/// <summary>Process-specific JSONL logs, bounded by size and retained file count.</summary>
public sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly int _retainedFiles;
    private readonly string _session = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}";
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    private int _segment;
    private long _bytes;
    private bool _disposed;

    public FileLoggerProvider(string directory, long maximumFileBytes = 5 * 1024 * 1024, int retainedFiles = 20)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFiles, 2);
        _directory = Path.GetFullPath(directory);
        _maximumFileBytes = maximumFileBytes;
        _retainedFiles = retainedFiles;
    }

    public string? LastWriteError { get; private set; }
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }

    private void Write<TState>(string category, LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                var scopes = new List<object?>();
                _scopes.ForEachScope(static (scope, list) => list.Add(scope), scopes);
                var entry = JsonSerializer.Serialize(new
                {
                    timestamp = DateTimeOffset.Now,
                    level = level.ToString(),
                    category,
                    eventId = eventId.Id,
                    eventName = eventId.Name,
                    processId = Environment.ProcessId,
                    threadId = Environment.CurrentManagedThreadId,
                    message = formatter(state, exception),
                    properties = state as IEnumerable<KeyValuePair<string, object?>>,
                    scopes,
                    exception = exception?.ToString(),
                }) + Environment.NewLine;
                var entryBytes = Encoding.UTF8.GetByteCount(entry);
                if (_bytes > 0 && _bytes + entryBytes > _maximumFileBytes)
                {
                    _segment++;
                    _bytes = 0;
                }
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"prw-{_session}-{_segment.ToString("D4", CultureInfo.InvariantCulture)}.jsonl");
                File.AppendAllText(path, entry, new UTF8Encoding(false));
                if (_bytes == 0)
                {
                    foreach (var oldFile in new DirectoryInfo(_directory).EnumerateFiles("prw-*.jsonl")
                        .OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name, StringComparer.Ordinal).Skip(_retainedFiles))
                    {
                        try { oldFile.Delete(); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                _bytes += entryBytes;
                LastWriteError = null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                LastWriteError = error.Message;
                Debug.WriteLine($"File logging failed: {error}");
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) provider.Write(category, logLevel, eventId, state, exception, formatter);
        }
    }
}
