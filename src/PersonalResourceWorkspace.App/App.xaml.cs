using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;
using PersonalResourceWorkspace.Infrastructure;
using PersonalResourceWorkspace.Windows;

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

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddWindowsPlatform();
        services.AddInfrastructure();
        services.AddSingleton<ResourceLaunchService>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            foreach (var initializer in _services.GetServices<IApplicationInitializer>())
            {
                await initializer.InitializeAsync(CancellationToken.None);
            }

            _window = _services.GetRequiredService<MainWindow>();
            _window.Activate();
        }
        catch (Exception exception)
        {
            LogInitializationFailure(_services.GetRequiredService<ILogger<App>>(), exception);
            Exit();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        LogUnhandledUiException(_services.GetRequiredService<ILogger<App>>(), args.Exception);
        args.Handled = false;
    }
}
