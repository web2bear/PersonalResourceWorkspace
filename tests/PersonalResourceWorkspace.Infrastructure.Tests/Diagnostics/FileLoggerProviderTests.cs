using System.Text.Json;
using Microsoft.Extensions.Logging;
using PersonalResourceWorkspace.Infrastructure.Diagnostics;

namespace PersonalResourceWorkspace.Infrastructure.Tests.Diagnostics;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prw-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParallelWritesPreserveEntriesScopesAndExceptions()
    {
        using var provider = new FileLoggerProvider(_root);
        var logger = provider.CreateLogger("Preview");
        Parallel.For(0, 64, index =>
        {
            using var scope = logger.BeginScope(new Dictionary<string, object> { ["ResourceId"] = index });
            logger.Log(LogLevel.Error, new EventId(7, "PreviewFailed"), index,
                new InvalidOperationException("decoder failed"), static (value, _) => $"preview {value}");
        });
        var lines = File.ReadAllLines(Directory.GetFiles(_root).Single());
        Assert.Equal(64, lines.Length);
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            Assert.Equal("Error", json.RootElement.GetProperty("level").GetString());
            Assert.Contains("decoder failed", json.RootElement.GetProperty("exception").GetString());
            Assert.True(json.RootElement.GetProperty("scopes")[0].TryGetProperty("ResourceId", out _));
        }
        Assert.Null(provider.LastWriteError);
    }

    [Fact]
    public void RotationLimitsRetainedFiles()
    {
        using var provider = new FileLoggerProvider(_root, maximumFileBytes: 600, retainedFiles: 3);
        var logger = provider.CreateLogger("Preview");
        for (var index = 0; index < 30; index++)
            logger.Log(LogLevel.Information, default, index, null, static (value, _) => $"preview {value}");
        var files = Directory.GetFiles(_root);
        Assert.Equal(3, files.Length);
        Assert.Contains(files.SelectMany(File.ReadAllLines), line => line.Contains("preview 29", StringComparison.Ordinal));
    }

    [Fact]
    public void UnwritableLogPathDoesNotBreakApplication()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "file");
        File.WriteAllText(file, "occupied");
        using var provider = new FileLoggerProvider(file);
        provider.CreateLogger("Preview").Log(LogLevel.Error, default, "failure", null, static (state, _) => state);
        Assert.NotNull(provider.LastWriteError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
