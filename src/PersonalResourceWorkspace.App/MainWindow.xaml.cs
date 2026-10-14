using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using PersonalResourceWorkspace.Application.Diagnostics;

namespace PersonalResourceWorkspace.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The semaphore lives for the lifetime of the main window and is released after its final save on close.")]
public sealed partial class MainWindow : Window
{
    private readonly MainPage _mainPage;
    private ExplorerFileDropHook? _explorerDropHook;
    private bool _dropHookScheduled;
    private readonly IUserPreferenceStore _userPreferences;
    private readonly DispatcherQueueTimer _placementSaveTimer;
    private readonly SemaphoreSlim _placementSaveGate = new(1, 1);
    private bool _placementTrackingEnabled;
    private WindowPlacement? _lastNormalPlacement;
    private const string PlacementKey = "main-window-placement";
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        ResourceLaunchService resourceLaunchService,
        IClipboardService clipboardService,
        IResourceVisualService resourceVisualService,
        IResourceViewPreferenceStore viewPreferenceStore,
        IUserPreferenceStore userPreferences,
        IPortableDataDirectory dataDirectory,
        IWorkspaceRepository workspaceRepository,
        ILoggerFactory loggerFactory)
    {
        InitializeComponent();
        _logger = loggerFactory.CreateLogger<MainWindow>();
        _userPreferences = userPreferences;
        _placementSaveTimer = DispatcherQueue.CreateTimer();
        _placementSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _placementSaveTimer.Tick += PlacementSaveTimer_Tick;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        _mainPage = new MainPage(this, resourceLaunchService, clipboardService, resourceVisualService, viewPreferenceStore, userPreferences, dataDirectory, workspaceRepository, loggerFactory.CreateLogger<MainPage>());
        RootFrame.Content = _mainPage;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;
    }

    public async Task RestorePlacementAsync()
    {
        try
        {
            var saved = await _userPreferences.GetAsync(PlacementKey, CancellationToken.None);
            var placement = saved is null ? null : JsonSerializer.Deserialize<WindowPlacement>(saved);
            if (placement is { Width: >= 320 and <= 10000, Height: >= 240 and <= 10000 })
            {
                var requested = new RectInt32(placement.X, placement.Y, placement.Width, placement.Height);
                var workArea = DisplayArea.GetFromRect(requested, DisplayAreaFallback.Nearest).WorkArea;
                var width = Math.Min(placement.Width, workArea.Width);
                var height = Math.Min(placement.Height, workArea.Height);
                var x = Math.Clamp(placement.X, workArea.X, workArea.X + workArea.Width - width);
                var y = Math.Clamp(placement.Y, workArea.Y, workArea.Y + workArea.Height - height);
                AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
                _lastNormalPlacement = new WindowPlacement(x, y, width, height);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.OperationFailed(_logger, "Restore window placement", null, exception);
            // Damaged or unavailable preferences must not prevent the main window from opening.
        }

        _placementTrackingEnabled = true;
        _lastNormalPlacement ??= CapturePlacement();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_placementTrackingEnabled || (!args.DidPositionChange && !args.DidSizeChange) ||
            sender.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored })
        {
            return;
        }

        _lastNormalPlacement = CapturePlacement();
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private async void PlacementSaveTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        await SavePlacementAsync();
    }

    private async Task SavePlacementAsync()
    {
        var value = JsonSerializer.Serialize(_lastNormalPlacement);
        await _placementSaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _userPreferences.SetAsync(PlacementKey, value, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DiagnosticLog.OperationFailed(_logger, "Save window placement", null, exception);
            // A failed preference write must not interrupt resizing or closing.
        }
        finally
        {
            _placementSaveGate.Release();
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_dropHookScheduled)
        {
            return;
        }

        _dropHookScheduled = true;
        AttachExplorerDropHook();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, AttachExplorerDropHook);
    }

    private void AttachExplorerDropHook()
    {
        _explorerDropHook?.Dispose();
        _explorerDropHook = ExplorerFileDropHook.Attach(this, _mainPage.AddDroppedPaths);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _placementSaveTimer.Stop();
        AppWindow.Changed -= AppWindow_Changed;
        if (_placementTrackingEnabled && _lastNormalPlacement is not null)
        {
            SavePlacementAsync().GetAwaiter().GetResult();
        }
        else
        {
            _placementSaveGate.Wait();
            _placementSaveGate.Release();
        }
        _placementSaveGate.Dispose();
        _explorerDropHook?.Dispose();
        _explorerDropHook = null;
    }

    private WindowPlacement CapturePlacement()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return new WindowPlacement(position.X, position.Y, size.Width, size.Height);
    }

    private sealed record WindowPlacement(int X, int Y, int Width, int Height);
}
