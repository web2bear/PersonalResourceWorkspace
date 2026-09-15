using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Resources;

namespace PersonalResourceWorkspace.App;

public sealed partial class MainWindow : Window
{
    private readonly MainPage _mainPage;
    private ExplorerFileDropHook? _explorerDropHook;
    private bool _dropHookScheduled;

    public MainWindow(
        IResourceRepository resourceRepository,
        ICollectionRepository collectionRepository,
        ResourceLaunchService resourceLaunchService,
        IClipboardService clipboardService,
        IResourceVisualService resourceVisualService,
        ICollectionViewPreferenceStore viewPreferenceStore)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        _mainPage = new MainPage(this, resourceRepository, collectionRepository, resourceLaunchService, clipboardService, resourceVisualService, viewPreferenceStore);
        RootFrame.Content = _mainPage;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
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
        _explorerDropHook?.Dispose();
        _explorerDropHook = null;
    }
}
