using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;
using PersonalResourceWorkspace.Infrastructure;
using PersonalResourceWorkspace.Windows;
using PersonalResourceWorkspace.Application.Diagnostics;
using PersonalResourceWorkspace.Infrastructure.Diagnostics;

namespace PersonalResourceWorkspace.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private static readonly Action<ILogger, Exception?> LogInitializationFailure =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(1, nameof(LogInitializationFailure)),
            "Application initialization failed.");

    private static readonly Action<ILogger, Exception?> LogUnhandledUiException =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(2, nameof(LogUnhandledUiException)),
            "Unhandled UI exception.");

    private readonly ServiceProvider _services;
    private Window? _window;
    private LogLevel _minimumLogLevel = LogLevel.Information;
    private LogLevel _previewLogLevel = LogLevel.Debug;
    private bool _fileLoggerAttached;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Trace)
            .AddFilter((category, level) => level >= (category is not null &&
                (category.Contains("Visuals", StringComparison.Ordinal) || category.EndsWith("MainPage", StringComparison.Ordinal))
                    ? _previewLogLevel : _minimumLogLevel)));
        services.AddWindowsPlatform();
        services.AddInfrastructure();
        services.AddSingleton<ResourceLaunchService>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider(validateScopes: true);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error)
                DiagnosticLog.OperationFailed(_services.GetRequiredService<ILogger<App>>(), "Unhandled process exception", null, error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
            DiagnosticLog.OperationFailed(_services.GetRequiredService<ILogger<App>>(), "Unobserved task exception", null, args.Exception);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var data = _services.GetRequiredService<IPortableDataDirectory>();
            await data.InitializeAsync(CancellationToken.None);
            AttachFileLogger(Path.Combine(data.GetLocalDataRoot(), "Logs"));
            DiagnosticLog.Startup(_services.GetRequiredService<ILogger<App>>(), Environment.ProcessPath ?? AppContext.BaseDirectory,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown", data.GetLocalDataRoot(), data.Source,
                Path.Combine(data.GetLocalDataRoot(), "Data", "workspace.db"));
            foreach (var initializer in _services.GetServices<IApplicationInitializer>())
            {
                await initializer.InitializeAsync(CancellationToken.None);
            }

            var preferences = _services.GetRequiredService<IUserPreferenceStore>();
            _minimumLogLevel = await LoadLogLevelAsync(preferences, "logging-level", LogLevel.Information);
            _previewLogLevel = await LoadLogLevelAsync(preferences, "logging-preview-level", LogLevel.Debug);

            _window = _services.GetRequiredService<MainWindow>();
            await ((MainWindow)_window).RestorePlacementAsync();
            _window.Activate();
            _window.Closed += (_, _) => DiagnosticLog.Closed(_services.GetRequiredService<ILogger<App>>());
        }
        catch (Exception exception)
        {
            if (!_fileLoggerAttached) AttachFileLogger(Path.Combine(AppContext.BaseDirectory, "Logs"));
            LogInitializationFailure(_services.GetRequiredService<ILogger<App>>(), exception);
            _window = new Window { Title = "Ошибка каталога данных" };
            _window.Content = new TextBlock
            {
                Text = $"Не удалось открыть данные приложения:\n{exception.Message}\n\nПроверьте --data-dir, PERSONAL_RESOURCE_WORKSPACE_DATA_DIR или portable-config.json.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
            };
            _window.Activate();
        }
    }

    private void AttachFileLogger(string directory)
    {
        _services.GetRequiredService<ILoggerFactory>().AddProvider(new FileLoggerProvider(directory));
        _fileLoggerAttached = true;
    }

    private static async Task<LogLevel> LoadLogLevelAsync(IUserPreferenceStore preferences, string key, LogLevel fallback)
    {
        var value = await preferences.GetAsync(key, CancellationToken.None);
        if (Enum.TryParse<LogLevel>(value, true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        await preferences.SetAsync(key, fallback.ToString(), CancellationToken.None);
        return fallback;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        LogUnhandledUiException(_services.GetRequiredService<ILogger<App>>(), args.Exception);
        args.Handled = false;
    }
}
