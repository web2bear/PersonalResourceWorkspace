using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Preferences;
using PersonalResourceWorkspace.Application.Resources;
using PersonalResourceWorkspace.Domain.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Microsoft.Extensions.Logging;
using PersonalResourceWorkspace.Application.Diagnostics;

namespace PersonalResourceWorkspace.App;

public sealed partial class MainPage : Page
{
    private readonly List<WorkspaceResource> _resources = [];

    private const string AllSearchTag = "all";
    private readonly List<TagRecord> _tags = [];
    private readonly List<SavedSearchRecord> _searches = [];
    private readonly HashSet<Guid> _temporaryTags = [];
    private Guid? _activeSearchId;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly ResourceLaunchService _resourceLaunchService;
    private readonly IClipboardService _clipboardService;
    private readonly IResourceVisualService _resourceVisualService;
    private readonly IResourceViewPreferenceStore _viewPreferenceStore;
    private readonly IUserPreferenceStore _userPreferences;
    private readonly IPortableDataDirectory _dataDirectory;
    private readonly Window _window;
    private readonly ILogger<MainPage> _logger;
    private ResourceViewMode _viewMode = ResourceViewMode.Grid;
    private readonly List<(ResourceViewMode Mode, ToggleMenuFlyoutItem Item)> _viewModeMenuItems = [];
    private static readonly (ResourceViewMode Mode, string Label, int CardSize)[] ViewModes =
    [
        (ResourceViewMode.GridSmall, "Сетка — маленькая", 0),
        (ResourceViewMode.Grid, "Сетка — средняя", 1),
        (ResourceViewMode.GridLarge, "Сетка — большая", 2),
        (ResourceViewMode.List, "Список", 1),
    ];
    private int _cardSize => ViewModes.First(option => option.Mode == _viewMode).CardSize;
    private int _maximumGridColumns = 8;
    private bool _showCardTitle = true;
    private bool _showCardDescription;
    private bool _showCardDeleteButton;
    private ResourceSortMode _sortMode = ResourceSortMode.Title;
    private IReadOnlyDictionary<Guid, long> _activeResourceRanks = new Dictionary<Guid, long>();
    private WorkspaceResource? _selectedResource;
    private bool _synchronizingSelection;
    private string? _lastDropSignature;
    private long _lastDropTicks;

    public ObservableCollection<WorkspaceResource> FilteredResources { get; } = [];

    public MainPage(
        Window window,
        ResourceLaunchService resourceLaunchService,
        IClipboardService clipboardService,
        IResourceVisualService resourceVisualService,
        IResourceViewPreferenceStore viewPreferenceStore,
        IUserPreferenceStore userPreferences,
        IPortableDataDirectory dataDirectory,
        IWorkspaceRepository workspaceRepository,
        ILogger<MainPage> logger)
    {
        _window = window;
        _logger = logger;
        _resourceLaunchService = resourceLaunchService;
        _clipboardService = clipboardService;
        _resourceVisualService = resourceVisualService;
        _viewPreferenceStore = viewPreferenceStore;
        _userPreferences = userPreferences;
        _dataDirectory = dataDirectory;
        _workspaceRepository = workspaceRepository;
        InitializeComponent();
        var viewMenu = new MenuFlyout();
        foreach (var option in ViewModes)
        {
            var item = new ToggleMenuFlyoutItem { Text = option.Label };
            var mode = option.Mode;
            item.Click += async (_, _) => await SetViewModeAsync(mode, persist: true);
            viewMenu.Items.Add(item);
            _viewModeMenuItems.Add((mode, item));
        }
        ViewModeButton.Flyout = viewMenu;
        UpdateViewModeMenu();
        if (ShellNavigation.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = "Настройки";
            AutomationProperties.SetName(settingsItem, "Настройки");
        }
        AddHandler(DragEnterEvent, new DragEventHandler(WorkArea_DragOver), true);
        AddHandler(DragOverEvent, new DragEventHandler(WorkArea_DragOver), true);
        AddHandler(DragLeaveEvent, new DragEventHandler(WorkArea_DragLeave), true);
        AddHandler(DropEvent, new DragEventHandler(WorkArea_Drop), true);
        Loaded += MainPage_Loaded;
        ApplyFilter();
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RestoreGridPreferencesAsync();
        await RestoreViewModeAsync();
        await RestoreSortModeAsync();
        await LoadWorkspaceNavAsync();
        await LoadResourcesAsync();
        UpdateResponsiveLayout(ActualWidth);
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromArgb(255, red, green, blue));

    private void LogOperationFailure(Exception exception, [CallerMemberName] string operation = "") =>
        DiagnosticLog.OperationFailed(_logger, operation, null, exception);

    private static Brush ThemeBrush(string resourceKey, Brush fallback) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
            ? brush
            : fallback;

    private async void ShellNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            await ShowSettingsAsync();
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        if (item.Tag is SavedSearchRecord search)
        {
            _activeSearchId = search.Id;
            PageTitle.Text = search.Name;
            await LoadResourceRanksAsync(search.Id);
        }
        else if (Equals(item.Tag, AllSearchTag))
        {
            _activeSearchId = null;
            _activeResourceRanks = new Dictionary<Guid, long>();
            PageTitle.Text = "Все ресурсы";
        }
        else return;
        _temporaryTags.Clear();
        SearchBox.Text = string.Empty;
        TagFilterBox.Text = string.Empty;
        UpdateTagFilterChips();
        UpdateCreateAvailability();
        SortManuallyItem.IsEnabled = _activeSearchId.HasValue;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplyFilter();
        }
    }

    private async void AddResource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ActiveRequiredTags().Concat(_temporaryTags).Contains(SystemTags.TrashId))
            {
                await ShowMessageAsync("Создание недоступно", "В поиске с условием «Корзина» нельзя создать ресурс.");
                return;
            }
            var draft = await TryReadClipboardDraftAsync();
            var resource = draft is null
                ? CreateEmptyResource()
                : ToWorkspaceResource(draft);
            resource.TagIds.UnionWith(NewResourceTags());

            if (await ShowResourceEditorAsync(resource, isNew: true))
            {
                _resources.Insert(0, resource);
                ApplyFilter();
                StatusText.Text = $"Добавлено: {resource.Title}";
            }
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось добавить ресурс: {exception.Message}";
        }
    }

    private async Task<ResourceDraft?> TryReadClipboardDraftAsync()
    {
        try
        {
            var snapshot = await _clipboardService.ReadAsync(CancellationToken.None);
            return ResourceDraftFactory.TryCreate(snapshot);
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            return null;
        }
    }

    private void Resources_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WorkspaceResource resource)
        {
            StatusText.Text = $"Выбрано: {resource.Title} · двойной клик открывает ресурс";
        }
    }

    private async void Resources_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var resource = FindResource(e.OriginalSource as DependencyObject) ?? GetVisibleSelectedResource();
        if (resource is not null)
        {
            e.Handled = true;
            await OpenResourceAsync(resource.Kind, resource.Address);
        }
    }

    private async void OpenResourceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceResource resource })
        {
            await OpenResourceAsync(resource.Kind, resource.Address);
        }
    }

    private async void EditResourceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceResource resource } &&
            await ShowResourceEditorAsync(resource, isNew: false))
        {
            ApplyFilter();
            await SaveResourcesAsync();
            StatusText.Text = $"Сохранено: {resource.Title}";
        }
    }

    private async void DeleteResourceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceResource resource })
        {
            await DeleteResourceAsync(resource);
        }
    }

    private async void DeleteResourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkspaceResource resource })
        {
            await DeleteResourceAsync(resource);
        }
    }

    private async void Resources_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var resource = GetVisibleSelectedResource();
        if (resource is null)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await OpenResourceAsync(resource.Kind, resource.Address);
        }
        else if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            await DeleteResourceAsync(resource);
        }
    }

    private static WorkspaceResource? FindResource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ContentControl { Content: WorkspaceResource resource })
            {
                return resource;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void Resources_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelection || sender is not ListViewBase list || list.SelectedItem is not WorkspaceResource resource)
        {
            return;
        }

        _selectedResource = resource;
        StatusText.Text = $"Выбрано: {resource.Title}";
        _synchronizingSelection = true;
        ResourcesGrid.SelectedItem = resource;
        ResourcesList.SelectedItem = resource;
        _synchronizingSelection = false;
    }

    private WorkspaceResource? GetVisibleSelectedResource() =>
        (_viewMode != ResourceViewMode.List ? ResourcesGrid.SelectedItem : ResourcesList.SelectedItem)
        as WorkspaceResource;

    private void UpdateViewModeMenu()
    {
        foreach (var (mode, item) in _viewModeMenuItems) item.IsChecked = mode == _viewMode;
        ViewModeButton.Label = ViewModes.First(option => option.Mode == _viewMode).Label;
    }

    private async Task RestoreViewModeAsync()
    {
        try
        {
            var storedMode = await _userPreferences.GetAsync("resource-view", CancellationToken.None);
            var mode = await _viewPreferenceStore.LoadAsync(CancellationToken.None);
            if (string.Equals(storedMode, ResourceViewModePreference.GridValue, StringComparison.OrdinalIgnoreCase))
            {
                var legacySize = await _userPreferences.GetAsync("grid-card-size", CancellationToken.None);
                mode = legacySize switch
                {
                    "0" => ResourceViewMode.GridSmall,
                    "2" => ResourceViewMode.GridLarge,
                    _ => ResourceViewMode.Grid,
                };
                await SetViewModeAsync(mode, persist: true);
            }
            else await SetViewModeAsync(mode, persist: false);
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            await SetViewModeAsync(ResourceViewMode.Grid, persist: false);
            StatusText.Text = $"Не удалось восстановить вид: {exception.Message}";
        }
    }

    private async Task SetViewModeAsync(ResourceViewMode mode, bool persist)
    {
        _viewMode = mode;
        UpdateViewModeMenu();
        ResourcesGrid.Visibility = mode != ResourceViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        ResourcesList.Visibility = mode == ResourceViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        UpdateResponsiveLayout(ActualWidth);

        _synchronizingSelection = true;
        ResourcesGrid.SelectedItem = _selectedResource;
        ResourcesList.SelectedItem = _selectedResource;
        _synchronizingSelection = false;

        if (persist)
        {
            try
            {
                await _viewPreferenceStore.SaveAsync(mode, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogOperationFailure(exception);
                StatusText.Text = $"Не удалось сохранить вид: {exception.Message}";
            }
        }
    }

    private async void SortByTitle_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = ResourceSortMode.Title;
        SortByTitleItem.IsChecked = true;
        SortByModifiedItem.IsChecked = false;
        SortManuallyItem.IsChecked = false;
        ApplyFilter();
        await SaveSortModeAsync();
    }

    private async void SortByModified_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = ResourceSortMode.Modified;
        SortByTitleItem.IsChecked = false;
        SortByModifiedItem.IsChecked = true;
        SortManuallyItem.IsChecked = false;
        ApplyFilter();
        await SaveSortModeAsync();
    }

    private async void SortManually_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSearchId is not { } searchId) return;
        await LoadResourceRanksAsync(searchId);
        _sortMode = ResourceSortMode.Manual;
        SortByTitleItem.IsChecked = false;
        SortByModifiedItem.IsChecked = false;
        SortManuallyItem.IsChecked = true;
        ApplyFilter();
        await SaveSortModeAsync();
    }

    private async Task LoadResourceRanksAsync(Guid searchId)
    {
        try { _activeResourceRanks = await _workspaceRepository.GetResourceRanksAsync(searchId, CancellationToken.None); }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось загрузить порядок карточек: {exception.Message}";
            _activeResourceRanks = new Dictionary<Guid, long>();
        }
    }

    private async Task RestoreSortModeAsync()
    {
        try
        {
            var savedMode = await _userPreferences.GetAsync("sort-mode", CancellationToken.None);
            _sortMode = savedMode switch
            {
                "modified" => ResourceSortMode.Modified,
                "manual" => ResourceSortMode.Manual,
                _ => ResourceSortMode.Title,
            };
            SortByTitleItem.IsChecked = _sortMode == ResourceSortMode.Title;
            SortByModifiedItem.IsChecked = _sortMode == ResourceSortMode.Modified;
            SortManuallyItem.IsChecked = _sortMode == ResourceSortMode.Manual;
            SortManuallyItem.IsEnabled = _activeSearchId.HasValue;
            ApplyFilter();
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось восстановить сортировку: {exception.Message}";
        }
    }

    private async Task SaveSortModeAsync()
    {
        try { await _userPreferences.SetAsync("sort-mode", _sortMode switch { ResourceSortMode.Modified => "modified", ResourceSortMode.Manual => "manual", _ => "title" }, CancellationToken.None); }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось сохранить сортировку: {exception.Message}";
        }
    }

    private async Task ShowSettingsAsync()
    {
        var rootLabel = new TextBlock { Text = $"Каталог данных (источник: {_dataDirectory.Source})" };
        var pathBox = new TextBox { Text = _dataDirectory.GetLocalDataRoot(), IsEnabled = !_dataDirectory.IsOverridden };
        var browse = new Button { Content = "Выбрать каталог", IsEnabled = !_dataDirectory.IsOverridden };
        browse.Click += async (_, _) =>
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            var selected = await picker.PickSingleFolderAsync();
            if (selected is not null) pathBox.Text = selected.Path;
        };
        var viewBox = new ComboBox { Header = "Вид ресурсов" };
        foreach (var option in ViewModes) viewBox.Items.Add(option.Label);
        viewBox.SelectedIndex = Array.FindIndex(ViewModes, option => option.Mode == _viewMode);
        var sortBox = new ComboBox { Header = "Сортировка" };
        sortBox.Items.Add("По названию"); sortBox.Items.Add("По дате изменения"); sortBox.Items.Add("Вручную (в сохранённом поиске)");
        sortBox.SelectedIndex = (int)_sortMode;
        var columnsBox = new ComboBox { Header = "Столбцов не более" };
        for (var columns = 2; columns <= 8; columns++) columnsBox.Items.Add(columns);
        columnsBox.SelectedIndex = _maximumGridColumns - 2;
        var titleSwitch = new ToggleSwitch { Header = "Показывать название карточки", IsOn = _showCardTitle };
        var descriptionSwitch = new ToggleSwitch { Header = "Показывать дополнительный текст", IsOn = _showCardDescription };
        var deleteSwitch = new ToggleSwitch { Header = "Показывать кнопку удаления", IsOn = _showCardDeleteButton };
        var logLevelBox = new ComboBox { Header = "Уровень журнала приложения", ItemsSource = Enum.GetValues<LogLevel>() };
        logLevelBox.SelectedItem = Enum.TryParse<LogLevel>(await _userPreferences.GetAsync("logging-level", CancellationToken.None), out var generalLevel) ? generalLevel : LogLevel.Information;
        var previewLogLevelBox = new ComboBox { Header = "Уровень журнала превью", ItemsSource = Enum.GetValues<LogLevel>() };
        previewLogLevelBox.SelectedItem = Enum.TryParse<LogLevel>(await _userPreferences.GetAsync("logging-preview-level", CancellationToken.None), out var previewLevel) ? previewLevel : LogLevel.Debug;
        var stack = new StackPanel { Spacing = 12, MinWidth = 400 };
        stack.Children.Add(rootLabel); stack.Children.Add(pathBox); stack.Children.Add(browse);
        stack.Children.Add(new TextBlock
        {
            Text = $"Файл базы данных: {Path.Combine(_dataDirectory.GetLocalDataRoot(), "Data", "workspace.db")}",
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
        if (_dataDirectory.IsOverridden)
            stack.Children.Add(new TextBlock { Text = "Путь задан извне. Измените параметр запуска или переменную окружения, чтобы выбрать другой каталог.", TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(viewBox); stack.Children.Add(sortBox);
        stack.Children.Add(new TextBlock { Text = "Сетка", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(columnsBox);
        stack.Children.Add(titleSwitch); stack.Children.Add(descriptionSwitch); stack.Children.Add(deleteSwitch);
        stack.Children.Add(new TextBlock { Text = "Диагностика", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(logLevelBox); stack.Children.Add(previewLogLevelBox);
        stack.Children.Add(new TextBlock { Text = $"Журнал: {Path.Combine(_dataDirectory.GetLocalDataRoot(), "Logs")}\nИзменения уровня применяются после перезапуска.", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        var dialog = new ContentDialog
        {
            Title = "Настройки", Content = new ScrollViewer { Content = stack, MaxHeight = 540 }, PrimaryButtonText = "Сохранить", CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var requestedRoot = Path.GetFullPath(Path.IsPathFullyQualified(pathBox.Text) ? pathBox.Text : Path.Combine(AppContext.BaseDirectory, pathBox.Text));
            var changedRoot = !string.Equals(requestedRoot, _dataDirectory.GetLocalDataRoot(), StringComparison.OrdinalIgnoreCase);
            await SetViewModeAsync(ViewModes[viewBox.SelectedIndex].Mode, persist: true);
            _sortMode = (ResourceSortMode)sortBox.SelectedIndex;
            SortByTitleItem.IsChecked = _sortMode == ResourceSortMode.Title;
            SortByModifiedItem.IsChecked = _sortMode == ResourceSortMode.Modified;
            SortManuallyItem.IsChecked = _sortMode == ResourceSortMode.Manual;
            ApplyFilter();
            await SaveSortModeAsync();
            _maximumGridColumns = columnsBox.SelectedIndex + 2;
            _showCardTitle = titleSwitch.IsOn;
            _showCardDescription = descriptionSwitch.IsOn;
            _showCardDeleteButton = deleteSwitch.IsOn;
            UpdateResponsiveLayout(ActualWidth);
            await SaveGridPreferencesAsync();
            await _userPreferences.SetAsync("logging-level", logLevelBox.SelectedItem.ToString()!, CancellationToken.None);
            await _userPreferences.SetAsync("logging-preview-level", previewLogLevelBox.SelectedItem.ToString()!, CancellationToken.None);
            if (changedRoot) await _dataDirectory.RelocateAsync(pathBox.Text, CancellationToken.None);
            StatusText.Text = changedRoot ? "Данные скопированы. Перезапустите приложение для перехода в новый каталог." : "Настройки сохранены.";
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось сохранить настройки: {exception.Message}";
        }
    }

    private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private async Task RestoreGridPreferencesAsync()
    {
        try
        {
            var columns = await _userPreferences.GetAsync("grid-max-columns", CancellationToken.None);
            _maximumGridColumns = int.TryParse(columns, out var parsedColumns) ? Math.Clamp(parsedColumns, 2, 8) : 8;
            _showCardTitle = await _userPreferences.GetAsync("grid-show-title", CancellationToken.None) != "false";
            _showCardDescription = await _userPreferences.GetAsync("grid-show-description", CancellationToken.None) == "true";
            _showCardDeleteButton = await _userPreferences.GetAsync("grid-show-delete", CancellationToken.None) == "true";
            UpdateResponsiveLayout(ActualWidth);
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось восстановить настройки сетки: {exception.Message}";
        }
    }

    private async Task SaveGridPreferencesAsync()
    {
        await _userPreferences.SetAsync("grid-max-columns", _maximumGridColumns.ToString(CultureInfo.InvariantCulture), CancellationToken.None);
        await _userPreferences.SetAsync("grid-show-title", _showCardTitle ? "true" : "false", CancellationToken.None);
        await _userPreferences.SetAsync("grid-show-description", _showCardDescription ? "true" : "false", CancellationToken.None);
        await _userPreferences.SetAsync("grid-show-delete", _showCardDeleteButton ? "true" : "false", CancellationToken.None);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var narrow = width < 720;
        WorkArea.Padding = narrow ? new Thickness(16, 14, 16, 16) : new Thickness(28, 20, 28, 22);
        DropHint.Visibility = width < 620 ? Visibility.Collapsed : Visibility.Visible;

        if (ResourcesGrid.ItemsPanelRoot is ItemsWrapGrid wrapGrid && ResourcesGrid.ActualWidth > 0)
        {
            var targetWidth = _cardSize switch { 0 => 184d, 2 => 304d, _ => 224d };
            var available = Math.Max(1, ResourcesGrid.ActualWidth - 4);
            var columns = Math.Clamp((int)Math.Floor((available + 12) / (targetWidth + 12)), 1, _maximumGridColumns);
            wrapGrid.ItemWidth = Math.Min(targetWidth, Math.Max(1, available - 12 * (columns - 1)) / columns);
            wrapGrid.ItemHeight = _cardSize switch { 0 => 174, 2 => 312, _ => 234 };
        }

        UpdateResourcePresentation(width);
    }

    private void WorkArea_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.ContainsKey("ResourceId") || e.DataView.Properties.ContainsKey("SearchId")) return;
        if (NewResourceTags().Contains(SystemTags.TrashId))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            DropOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Добавить в Personal Space";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        DropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void WorkArea_DragLeave(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.ContainsKey("ResourceId") || e.DataView.Properties.ContainsKey("SearchId")) return;
        var position = e.GetPosition(WorkArea);
        if (position.X < 0 ||
            position.Y < 0 ||
            position.X > WorkArea.ActualWidth ||
            position.Y > WorkArea.ActualHeight)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        e.Handled = true;
    }

    private async void WorkArea_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.ContainsKey("ResourceId") || e.DataView.Properties.ContainsKey("SearchId")) return;
        if (NewResourceTags().Contains(SystemTags.TrashId))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            StatusText.Text = "В поиске с условием «Корзина» нельзя создать ресурс";
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
        DropOverlay.Visibility = Visibility.Collapsed;

        var deferral = e.GetDeferral();
        try
        {
            await AddDroppedResourcesAsync(e.DataView);
        }
        finally
        {
            deferral.Complete();
        }
    }

    internal void AddDroppedPaths(IReadOnlyList<string> paths)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            _ = AddDroppedPathsAsync(paths);
            return;
        }

        DispatcherQueue.TryEnqueue(() => _ = AddDroppedPathsAsync(paths));
    }

    private async Task AddDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        var added = new List<WorkspaceResource>();
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                added.Add(ToWorkspaceResource(ResourceDraftFactory.FromPath(path)));
            }
        }

        await AddResourcesAsync(added);
    }

    private async Task<bool> AddDroppedResourcesAsync(DataPackageView dataView)
    {
        try
        {
            return await AddResourcesAsync(await CollectDroppedResourcesAsync(dataView));
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось добавить: {exception.Message}";
            return false;
        }
    }

    private async Task<bool> AddResourcesAsync(List<WorkspaceResource> added)
    {
        if (NewResourceTags().Contains(SystemTags.TrashId))
        {
            StatusText.Text = "В поиске с условием «Корзина» нельзя создать ресурс";
            return false;
        }
        if (IsDuplicateDrop(added))
        {
            return true;
        }

        foreach (var resource in added)
        {
            resource.TagIds.UnionWith(NewResourceTags());
            _resources.Insert(0, resource);
        }

        ApplyFilter();
        StatusText.Text = added.Count switch
        {
            0 => "Не удалось добавить ресурсы",
            1 => "Ресурс добавлен. Нажмите карточку, чтобы отредактировать",
            _ => $"Добавлено ресурсов: {added.Count}. Нажмите карточку, чтобы отредактировать",
        };

        if (added.Count == 0)
        {
            return false;
        }

        await SaveResourcesAsync();
        _ = ResolveVisualsAsync(added, forceRefresh: false);
        return true;
    }

    private bool IsDuplicateDrop(List<WorkspaceResource> added)
    {
        if (added.Count == 0)
        {
            return false;
        }

        var signature = string.Join('|', added.Select(resource => resource.Address));
        var ticks = Environment.TickCount64;
        if (signature == _lastDropSignature && ticks - _lastDropTicks < 1000)
        {
            return true;
        }

        _lastDropSignature = signature;
        _lastDropTicks = ticks;
        return false;
    }

    private async Task<List<WorkspaceResource>> CollectDroppedResourcesAsync(
        DataPackageView dataView)
    {
        var added = new List<WorkspaceResource>();

        try
        {
            var items = await dataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                added.Add(CreateResourceFromStorageItem(item));
            }

            if (added.Count > 0)
            {
                return added;
            }
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            // Unpackaged / island payloads often expose CF_HDROP without a pre-populated StorageItems format.
        }

        if (dataView.Contains(StandardDataFormats.Uri))
        {
            added.Add(ToWorkspaceResource(ResourceDraftFactory.FromUri(await dataView.GetUriAsync())));
            return added;
        }

        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            added.Add(ToWorkspaceResource(ResourceDraftFactory.FromUri(await dataView.GetWebLinkAsync())));
            return added;
        }

        if (dataView.Contains(StandardDataFormats.Text))
        {
            var text = (await dataView.GetTextAsync()).Trim();
            if (ResourceDraftFactory.TryParseText(text) is { } draft)
            {
                added.Add(ToWorkspaceResource(draft));
            }
        }

        return added;
    }

    private async Task<bool> ShowResourceEditorAsync(WorkspaceResource resource, bool isNew)
    {
        var titleBox = new TextBox
        {
            Header = "Название",
            Text = resource.Title,
            PlaceholderText = "Например, материалы проекта",
        };
        var descriptionBox = new TextBox
        {
            Header = "Описание",
            Text = resource.Description,
            PlaceholderText = "Короткое описание ресурса",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76,
        };
        var kindBox = new ComboBox
        {
            Header = "Тип",
            ItemsSource = StoredResourceKinds.EditorKinds,
            SelectedItem = ResourceTypeMapper.NormalizeStoredKind(resource.Kind),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var addressBox = new TextBox
        {
            Header = "Адрес ресурса",
            Text = resource.Address,
            PlaceholderText = "Путь к файлу или папке, URL либо адрес заметки",
        };
        var addressHint = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.65,
            Margin = new Thickness(0, -8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        var validationText = new TextBlock
        {
            Foreground = Brush(226, 90, 90),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var workingTags = resource.TagIds.ToHashSet();
        var tagInput = new AutoSuggestBox { PlaceholderText = "Введите имя тэга и нажмите Enter", Width = 330 };
        var tagChips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        void RenderEditorTags()
        {
            tagChips.Children.Clear();
            foreach (var id in workingTags)
            {
                var chip = new Button { Content = $"{TagName(id)} {(id == SystemTags.TrashId ? "" : "×")}" };
                chip.IsEnabled = id != SystemTags.TrashId;
                chip.Click += (_, _) =>
                {
                    workingTags.Remove(id);
                    if (id != SystemTags.InboxId && !workingTags.Contains(SystemTags.TrashId) &&
                        workingTags.All(tagId => tagId == SystemTags.InboxId)) workingTags.Add(SystemTags.InboxId);
                    RenderEditorTags();
                };
                tagChips.Children.Add(chip);
            }
        }
        tagInput.TextChanged += (sender, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                sender.ItemsSource = _tags.Where(tag => tag.Id != SystemTags.TrashId &&
                    tag.Name.Contains(sender.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    .Select(tag => tag.Name).ToArray();
        };
        tagInput.QuerySubmitted += async (sender, args) =>
        {
            try
            {
                var name = (args.ChosenSuggestion as string ?? args.QueryText).Trim();
                if (name.Length == 0) return;
                var tag = await GetOrCreateTagAsync(name);
                if (tag.Id == SystemTags.TrashId) return;
                if (tag.Id != SystemTags.InboxId && workingTags.All(id => id == SystemTags.InboxId))
                    workingTags.Remove(SystemTags.InboxId);
                workingTags.Add(tag.Id);
                sender.Text = string.Empty;
                RenderEditorTags();
            }
            catch (Exception exception)
            {
                LogOperationFailure(exception);
                validationText.Text = exception.Message; validationText.Visibility = Visibility.Visible;
            }
        };
        RenderEditorTags();

        var autoVisualOption = new ComboBoxItem { Content = "Автоматически", Tag = ResourceVisualMode.Auto };
        var userVisualOption = new ComboBoxItem { Content = "Своё изображение или иконка", Tag = ResourceVisualMode.UserAsset };
        var fallbackVisualOption = new ComboBoxItem { Content = "Значок типа", Tag = ResourceVisualMode.TypeFallback };
        var visualModeBox = new ComboBox
        {
            Header = "Визуал",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { autoVisualOption, userVisualOption, fallbackVisualOption },
            SelectedItem = resource.Visual.Mode switch
            {
                ResourceVisualMode.UserAsset => userVisualOption,
                ResourceVisualMode.TypeFallback => fallbackVisualOption,
                _ => autoVisualOption,
            },
        };
        var previewImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed,
        };
        var previewGlyph = new FontIcon
        {
            Glyph = resource.Glyph,
            FontSize = 36,
            Foreground = Brush(255, 255, 255),
        };
        var previewText = new TextBlock
        {
            Margin = new Thickness(18),
            MaxLines = 3,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed,
        };
        var previewProgress = new ProgressRing
        {
            Width = 28,
            Height = 28,
            IsActive = false,
            Visibility = Visibility.Collapsed,
        };
        var previewSurface = new Border
        {
            Height = 116,
            Background = resource.AccentBrush,
            CornerRadius = new CornerRadius(10),
            Child = new Grid
            {
                Children = { previewGlyph, previewImage, previewText, previewProgress },
            },
        };
        var visualStatus = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.72,
            Text = "Значок типа",
            TextWrapping = TextWrapping.Wrap,
        };
        var chooseVisualButton = new Button
        {
            Content = "Выбрать файл…",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var refreshVisualButton = new Button
        {
            Content = "Обновить автоматически",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var visualButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        visualButtons.Children.Add(chooseVisualButton);
        visualButtons.Children.Add(refreshVisualButton);

        string? pendingUserVisualPath = null;
        ResourceVisualResolution? editorResolution = null;
        CancellationTokenSource? previewCancellation = null;

        ResourceVisualMode SelectedVisualMode() =>
            (visualModeBox.SelectedItem as ComboBoxItem)?.Tag is ResourceVisualMode mode
                ? mode
                : ResourceVisualMode.Auto;

        void ShowFallbackPreview(string status)
        {
            previewImage.Source = null;
            previewImage.Visibility = Visibility.Collapsed;
            previewText.Visibility = Visibility.Collapsed;
            previewGlyph.Glyph = ResourceTypeMapper.GlyphForStoredKind(kindBox.SelectedItem as string);
            previewGlyph.Visibility = Visibility.Visible;
            previewProgress.IsActive = false;
            previewProgress.Visibility = Visibility.Collapsed;
            visualStatus.Text = status;
        }

        async Task ShowImagePreviewAsync(string path, ResourceVisualKind kind, CancellationToken cancellationToken)
        {
            var image = await LoadImageSourceAsync(path, cancellationToken);
            previewImage.Source = image;
            previewImage.Stretch = kind == ResourceVisualKind.Icon ? Stretch.Uniform : Stretch.UniformToFill;
            previewImage.Margin = kind == ResourceVisualKind.Icon ? new Thickness(10) : new Thickness(0);
            previewSurface.Background = kind == ResourceVisualKind.Icon
                ? ThemeBrush("CardBackgroundFillColorDefaultBrush", resource.AccentBrush)
                : AccentFor(kindBox.SelectedItem as string ?? resource.Kind);
            previewImage.Visibility = Visibility.Visible;
            previewGlyph.Visibility = Visibility.Collapsed;
            previewText.Visibility = Visibility.Collapsed;
        }

        async Task UpdateVisualPreviewAsync(bool forceRefresh)
        {
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            previewCancellation = new CancellationTokenSource();
            var cancellationToken = previewCancellation.Token;
            var mode = SelectedVisualMode();
            chooseVisualButton.IsEnabled = mode == ResourceVisualMode.UserAsset;
            refreshVisualButton.IsEnabled = mode == ResourceVisualMode.Auto;
            previewSurface.Background = AccentFor(kindBox.SelectedItem as string ?? resource.Kind);

            try
            {
                if (mode == ResourceVisualMode.TypeFallback)
                {
                    editorResolution = null;
                    ShowFallbackPreview("Будет использован значок типа");
                    return;
                }

                if (mode == ResourceVisualMode.UserAsset)
                {
                    var path = pendingUserVisualPath ?? _resourceVisualService.GetAbsoluteAssetPath(resource.Visual.AssetPath);
                    if (path is null || !File.Exists(path))
                    {
                        ShowFallbackPreview("Выберите локальное изображение или иконку");
                        return;
                    }

                    await ShowImagePreviewAsync(path, ResourceVisualKind.Image, cancellationToken);
                    visualStatus.Text = "Пользовательское изображение";
                    return;
                }

                ShowFallbackPreview("Получение визуала…");
                previewProgress.IsActive = true;
                previewProgress.Visibility = Visibility.Visible;
                previewGlyph.Visibility = Visibility.Collapsed;
                editorResolution = await _resourceVisualService.ResolveAsync(
                    new ResourceVisualRequest(
                        resource.Id,
                        ResourceTypeMapper.FromStoredKind(kindBox.SelectedItem as string),
                        addressBox.Text.Trim(),
                        descriptionBox.Text.Trim(),
                        resource.Visual.Mode == ResourceVisualMode.Auto ? resource.Visual : ResourceVisualDescriptor.Auto),
                    forceRefresh,
                    cancellationToken);

                if (editorResolution.AbsoluteAssetPath is { } assetPath)
                {
                    await ShowImagePreviewAsync(assetPath, editorResolution.Descriptor.Kind, cancellationToken);
                }
                else if (editorResolution.Descriptor.Kind == ResourceVisualKind.GeneratedPreview &&
                         !string.IsNullOrWhiteSpace(editorResolution.PreviewText))
                {
                    previewGlyph.Visibility = Visibility.Collapsed;
                    previewImage.Visibility = Visibility.Collapsed;
                    previewText.Text = editorResolution.PreviewText;
                    previewText.Visibility = Visibility.Visible;
                }
                else
                {
                    ShowFallbackPreview("Не удалось получить визуал — будет использован значок типа");
                }

                previewProgress.IsActive = false;
                previewProgress.Visibility = Visibility.Collapsed;
                visualStatus.Text = editorResolution.SourceLabel;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                LogOperationFailure(exception);
                ShowFallbackPreview($"Не удалось показать визуал: {exception.Message}");
            }
        }

        void UpdateAddressHint()
        {
            var isWeb = ResourceTypeMapper.FromStoredKind(kindBox.SelectedItem as string) == ResourceType.Web;
            addressBox.Header = isWeb ? "URL" : "Адрес ресурса";
            addressBox.PlaceholderText = isWeb
                ? "https://example.com"
                : "Путь к файлу или папке, URL либо адрес заметки";
            addressHint.Text = isWeb
                ? "Ссылка откроется в браузере по умолчанию."
                : "Этот адрес используется командой «Открыть».";
        }

        UpdateAddressHint();
        kindBox.SelectionChanged += (_, _) =>
        {
            validationText.Visibility = Visibility.Collapsed;
            UpdateAddressHint();
            _ = UpdateVisualPreviewAsync(forceRefresh: false);
        };

        addressBox.LostFocus += (_, _) =>
        {
            if (SelectedVisualMode() == ResourceVisualMode.Auto)
            {
                _ = UpdateVisualPreviewAsync(forceRefresh: false);
            }
        };
        visualModeBox.SelectionChanged += (_, _) => _ = UpdateVisualPreviewAsync(forceRefresh: false);
        refreshVisualButton.Click += (_, _) => _ = UpdateVisualPreviewAsync(forceRefresh: true);
        chooseVisualButton.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".tif", ".tiff" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                pendingUserVisualPath = file.Path;
                visualModeBox.SelectedItem = userVisualOption;
                await UpdateVisualPreviewAsync(forceRefresh: false);
            }
        };

        var fields = new StackPanel { Spacing = 14, MinWidth = 420 };
        fields.Children.Add(titleBox);
        fields.Children.Add(descriptionBox);
        fields.Children.Add(new TextBlock { Text = "Тэги" });
        fields.Children.Add(tagChips);
        fields.Children.Add(tagInput);
        fields.Children.Add(kindBox);
        fields.Children.Add(addressBox);
        fields.Children.Add(addressHint);
        fields.Children.Add(visualModeBox);
        fields.Children.Add(previewSurface);
        fields.Children.Add(visualStatus);
        fields.Children.Add(visualButtons);
        fields.Children.Add(validationText);

        var scrollableFields = new ScrollViewer
        {
            Content = fields,
            MaxHeight = 520,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? "Добавить ресурс" : "Редактировать ресурс",
            Content = scrollableFields,
            PrimaryButtonText = isNew ? "Добавить" : "Сохранить",
            SecondaryButtonText = "Открыть",
            IsSecondaryButtonEnabled = true,
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
        };

        ResourceVisualDescriptor committedVisual = resource.Visual;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var kind = ResourceTypeMapper.NormalizeStoredKind(kindBox.SelectedItem as string);
            var title = titleBox.Text.Trim();
            var address = addressBox.Text.Trim();
            if (ResourceTypeMapper.FromStoredKind(kind) == ResourceType.Web)
            {
                if (!WebUri.TryCreate(address, out var webUri))
                {
                    args.Cancel = true;
                    validationText.Text = "Укажите корректную HTTP или HTTPS ссылку, например https://example.com";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }

                addressBox.Text = webUri.AbsoluteUri;
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = webUri.Value.Host;
                    titleBox.Text = title;
                }

                validationText.Visibility = Visibility.Collapsed;
            }

            if (args.Cancel)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                args.Cancel = true;
                validationText.Text = "Введите название ресурса.";
                validationText.Visibility = Visibility.Visible;
                return;
            }

            var mode = SelectedVisualMode();
            if (mode == ResourceVisualMode.UserAsset)
            {
                if (pendingUserVisualPath is null && resource.Visual.Mode != ResourceVisualMode.UserAsset)
                {
                    args.Cancel = true;
                    validationText.Text = "Выберите изображение или переключите режим визуала.";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }

                if (pendingUserVisualPath is not null)
                {
                    var deferral = args.GetDeferral();
                    try
                    {
                        committedVisual = await _resourceVisualService.ImportUserAssetAsync(pendingUserVisualPath, CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        LogOperationFailure(exception);
                        args.Cancel = true;
                        validationText.Text = exception.Message;
                        validationText.Visibility = Visibility.Visible;
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                }
            }
            else if (mode == ResourceVisualMode.TypeFallback)
            {
                committedVisual = ResourceVisualDescriptor.Fallback;
            }
            else
            {
                committedVisual = editorResolution?.Descriptor.Mode == ResourceVisualMode.Auto
                    ? editorResolution.Descriptor
                    : ResourceVisualDescriptor.Auto;
            }
        };

        _ = UpdateVisualPreviewAsync(forceRefresh: false);
        var result = await dialog.ShowAsync();
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        if (result == ContentDialogResult.Secondary)
        {
            await OpenResourceAsync(kindBox.SelectedItem as string ?? resource.Kind, addressBox.Text);
            return false;
        }

        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(titleBox.Text))
        {
            return false;
        }

        resource.Title = titleBox.Text.Trim();
        resource.Description = descriptionBox.Text.Trim();
        resource.Kind = ResourceTypeMapper.NormalizeStoredKind(kindBox.SelectedItem as string);
        resource.Address = addressBox.Text.Trim();
        resource.TagIds = workingTags;
        resource.Glyph = ResourceTypeMapper.GlyphForStoredKind(resource.Kind);
        resource.AccentBrush = AccentFor(resource.Kind);
        resource.Visual = committedVisual;
        await SaveResourceAsync(resource);
        _ = ResolveVisualAsync(resource, forceRefresh: false);
        return true;
    }

    private async Task LoadResourcesAsync()
    {
        try
        {
            var saved = await _workspaceRepository.GetResourcesAsync(CancellationToken.None);
            DiagnosticLog.ResourcesLoaded(_logger, saved.Count);
            _resources.Clear();
            _resources.AddRange(saved.Select(resource => new WorkspaceResource(
                resource.Title,
                resource.Description,
                ResourceTypeMapper.NormalizeStoredKind(resource.Kind),
                resource.Modified,
                ResourceTypeMapper.GlyphForStoredKind(resource.Kind),
                AccentFor(resource.Kind),
                resource.Address,
                resource.Visual)
            { Id = resource.Id, TagIds = resource.TagIds.ToHashSet() }));
            ApplyFilter();
            await ResolveVisualsAsync(_resources, forceRefresh: false);
        }
        catch (Exception exception)
        {
            DiagnosticLog.OperationFailed(_logger, "Load resources", null, exception);
            StatusText.Text = $"Не удалось загрузить ресурсы: {exception.Message}";
        }
    }

    private async Task SaveResourceAsync(WorkspaceResource resource) =>
        await _workspaceRepository.SaveResourceAsync(
            new TaggedResourceRecord(
                resource.Id,
                resource.Title,
                resource.Description,
                resource.Kind,
                resource.Modified,
                resource.Glyph,
                resource.Address,
                resource.Visual,
                resource.TagIds.ToArray()),
            CancellationToken.None);

    private async Task SaveResourcesAsync()
    {
        foreach (var resource in _resources)
        {
            await SaveResourceAsync(resource);
        }
    }

    private async Task ResolveVisualsAsync(IEnumerable<WorkspaceResource> resources, bool forceRefresh)
    {
        await Task.WhenAll(resources.Select(resource => ResolveVisualAsync(resource, forceRefresh)));
    }

    private async Task ResolveVisualAsync(WorkspaceResource resource, bool forceRefresh)
    {
        resource.IsVisualLoading = true;
        try
        {
            var previousDescriptor = resource.Visual;
            var resolution = await _resourceVisualService.ResolveAsync(
                new ResourceVisualRequest(
                    resource.Id,
                    ResourceTypeMapper.FromStoredKind(resource.Kind),
                    resource.Address,
                    resource.Description,
                    resource.Visual),
                forceRefresh,
                CancellationToken.None);

            ImageSource? imageSource = null;
            if (resolution.AbsoluteAssetPath is { } path)
            {
                imageSource = await LoadImageSourceAsync(path, CancellationToken.None);
            }

            resource.ApplyVisualResolution(resolution, imageSource);
            DiagnosticLog.PreviewDisplayed(_logger, resource.Id, imageSource is not null, previousDescriptor != resolution.Descriptor);
            if (previousDescriptor != resolution.Descriptor)
            {
                await SaveResourceAsync(resource);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.OperationFailed(_logger, "Load or persist preview", resource.Id, exception);
            resource.ApplyVisualResolution(
                new ResourceVisualResolution(
                    resource.Visual,
                    null,
                    null,
                    "Значок типа"),
                null);
        }
        finally
        {
            resource.IsVisualLoading = false;
        }
    }

    private static async Task<ImageSource> LoadImageSourceAsync(string path, CancellationToken cancellationToken)
    {
        using var fileStream = File.OpenRead(path);
        using var stream = fileStream.AsRandomAccessStream();
        cancellationToken.ThrowIfCancellationRequested();
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return image;
    }

    private async Task OpenResourceAsync(string kind, string address)
    {
        try
        {
            await _resourceLaunchService.LaunchAsync(kind, address, CancellationToken.None);
            StatusText.Text = ResourceTypeMapper.FromStoredKind(kind) == ResourceType.TextSnippet
                ? "Текст заметки скопирован в буфер обмена"
                : $"Открывается: {address.Trim()}";
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = ResourceTypeMapper.FromStoredKind(kind) == ResourceType.TextSnippet
                    ? "Не удалось скопировать заметку"
                    : "Не удалось открыть ресурс",
                Content = exception.Message,
                CloseButtonText = "Закрыть",
            };
            await errorDialog.ShowAsync();
        }
    }

    private bool IsInTrash(WorkspaceResource resource) => resource.TagIds.Contains(SystemTags.TrashId);

    private static WorkspaceResource CreateEmptyResource() =>
        ToWorkspaceResource(
            new ResourceDraft(
                string.Empty,
                string.Empty,
                StoredResourceKinds.File,
                string.Empty));

    private static WorkspaceResource ToWorkspaceResource(ResourceDraft draft) =>
        new(
            draft.Title,
            draft.Description,
            draft.Kind,
            "только что",
            ResourceTypeMapper.GlyphForStoredKind(draft.Kind),
            AccentFor(draft.Kind),
            draft.Address);

    private static WorkspaceResource CreateResourceFromStorageItem(IStorageItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            return ToWorkspaceResource(ResourceDraftFactory.FromPath(item.Path));
        }

        var kind = item is StorageFolder ? StoredResourceKinds.Folder : StoredResourceKinds.File;
        return ToWorkspaceResource(
            new ResourceDraft(item.Name, string.Empty, kind, item.Path));
    }

    private void ApplyFilter()
    {
        foreach (var resource in _resources)
            resource.TagsDisplay = string.Join(", ", resource.TagIds.Where(id => id != SystemTags.TrashId).Select(TagName));
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var requiredTagIds = ActiveRequiredTags();
        IEnumerable<WorkspaceResource> matches = _resources.Where(resource =>
            SystemTags.Matches(resource.TagIds, requiredTagIds, _temporaryTags) &&
            (query.Length == 0 ||
             resource.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             resource.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
              resource.Address.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
              resource.Kind.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        matches = _sortMode == ResourceSortMode.Manual && _activeSearchId.HasValue
            ? matches.OrderBy(resource => _activeResourceRanks.TryGetValue(resource.Id, out var rank) ? rank : long.MaxValue)
                .ThenBy(resource => resource.Title, StringComparer.CurrentCultureIgnoreCase)
            : _sortMode == ResourceSortMode.Modified
            ? matches.OrderByDescending(resource => resource.Modified, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(resource => resource.Title, StringComparer.CurrentCultureIgnoreCase)
            : matches.OrderBy(resource => resource.Title, StringComparer.CurrentCultureIgnoreCase);

        _synchronizingSelection = true;
        FilteredResources.Clear();
        foreach (var resource in matches)
        {
            FilteredResources.Add(resource);
        }
        _synchronizingSelection = false;

        PageCountText.Text = FormatResourceCount(FilteredResources.Count);
        if (FilteredResources.Count == 0)
        {
            StatusText.Text = "Ресурсы не найдены";
        }

        if (_selectedResource is not null && FilteredResources.Contains(_selectedResource))
        {
            _synchronizingSelection = true;
            ResourcesGrid.SelectedItem = _selectedResource;
            ResourcesList.SelectedItem = _selectedResource;
            _synchronizingSelection = false;
        }
        else
        {
            _selectedResource = null;
        }

        UpdateSearchCounts();
        UpdateResourcePresentation(ActualWidth);
    }

    private static string FormatResourceCount(int count) => count switch
    {
        1 => "1 ресурс",
        >= 2 and <= 4 => $"{count} ресурса",
        _ => $"{count} ресурсов",
    };

    private void UpdateSearchCounts()
    {
        AllResourcesCount.Text = _resources.Count(resource => !resource.TagIds.Contains(SystemTags.TrashId)).ToString(CultureInfo.CurrentCulture);
        foreach (var search in _searches)
        {
            if (_searchNavItems.TryGetValue(search.Id, out var item))
                item.Content = CreateSearchDropContent(search.Name,
                    _resources.Count(resource => SystemTags.Matches(resource.TagIds, search.RequiredTagIds, [])));
        }
    }

    private void UpdateResourcePresentation(double width)
    {
        foreach (var resource in _resources)
        {
            resource.UpdatePresentation(
                showSourceColumn: width >= 660,
                showTagsColumn: width >= 850,
                showDateColumn: width >= 1020);
            resource.UpdateCardPresentation(_cardSize, _showCardTitle, _showCardDescription, _showCardDeleteButton);
        }
    }

    private async Task DeleteResourceAsync(WorkspaceResource resource)
    {
        try
        {
            var isAlreadyInTrash = IsInTrash(resource);
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = isAlreadyInTrash ? "Удалить ресурс безвозвратно?" : "Удалить ресурс?",
                Content = isAlreadyInTrash
                    ? $"Ресурс «{resource.Title}» будет удалён полностью и его нельзя будет восстановить."
                    : $"Ресурс «{resource.Title}» будет перемещён в «Корзину». Остальные тэги сохранятся.",
                PrimaryButtonText = isAlreadyInTrash ? "Удалить безвозвратно" : "Удалить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (isAlreadyInTrash)
            {
                await _workspaceRepository.DeleteResourceAsync(resource.Id, CancellationToken.None);
                _resources.Remove(resource);
                ApplyFilter();
                StatusText.Text = $"Удалено безвозвратно: {resource.Title}";
                return;
            }

            resource.TagIds.Add(SystemTags.TrashId);
            await SaveResourceAsync(resource);
            ApplyFilter();
            StatusText.Text = $"Перемещено в корзину: {resource.Title}";
        }
        catch (Exception exception)
        {
            LogOperationFailure(exception);
            StatusText.Text = $"Не удалось удалить ресурс: {exception.Message}";
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Закрыть",
        };
        await dialog.ShowAsync();
    }

    private static Grid CreateNavigationContent(string name, int count)
    {
        var grid = new Grid
        {
            MinWidth = 174,
            ColumnSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        var countText = new TextBlock
        {
            Text = count.ToString(CultureInfo.CurrentCulture),
            Foreground = ThemeBrush("TextFillColorTertiaryBrush", Brush(128, 128, 128)),
        };
        Grid.SetColumn(countText, 1);
        grid.Children.Add(countText);
        return grid;
    }

    private static SolidColorBrush AccentFor(string kind)
    {
        switch (ResourceTypeMapper.FromStoredKind(kind))
        {
            case ResourceType.Application:
                return Brush(88, 157, 234);
            case ResourceType.Web:
                return Brush(70, 184, 166);
            case ResourceType.File:
                return Brush(88, 157, 234);
            case ResourceType.Folder:
                return Brush(225, 178, 62);
            case ResourceType.TextSnippet:
                return Brush(153, 126, 224);
            case ResourceType.ImageSnippet:
                return Brush(232, 132, 83);
            case ResourceType.Command:
                return Brush(109, 92, 231);
            default:
                ResourceType unreachable = ResourceTypeMapper.FromStoredKind(kind);
                throw new InvalidOperationException($"Unknown resource type: {unreachable}");
        }
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 6)
        {
            return new SolidColorBrush(Color.FromArgb(
                255,
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value[2..4], 16),
                Convert.ToByte(value[4..6], 16)));
        }

        if (value.Length == 8)
        {
            return new SolidColorBrush(Color.FromArgb(
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value[2..4], 16),
                Convert.ToByte(value[4..6], 16),
                Convert.ToByte(value[6..8], 16)));
        }

        return Brush(126, 167, 255);
    }

    private enum ResourceSortMode
    {
        Title,
        Modified,
        Manual,
    }
}

public sealed class WorkspaceResource : INotifyPropertyChanged
{
    public WorkspaceResource(
        string title,
        string description,
        string kind,
        string modified,
        string glyph,
        SolidColorBrush accentBrush,
        string address = "",
        ResourceVisualDescriptor? visual = null)
    {
        Id = Guid.NewGuid();
        Title = title;
        Description = description;
        Kind = kind;
        Modified = modified;
        Glyph = glyph;
        AccentBrush = accentBrush;
        Address = address;
        _visual = visual ?? ResourceVisualDescriptor.Auto;
    }

    private ResourceVisualDescriptor _visual;
    private ImageSource? _visualImage;
    private bool _isVisualLoading;
    private string _visualPreviewText = string.Empty;
    private string _visualSourceLabel = "Значок типа";

    public string Title { get; set; }
    public string Description { get; set; }
    public string Kind { get; set; }
    public HashSet<Guid> TagIds { get; set; } = [];
    public string TagsDisplay { get; set; } = string.Empty;
    public string Modified { get; set; }
    public string Glyph { get; set; }
    public SolidColorBrush AccentBrush { get; set; }
    public string Address { get; set; }
    public Guid Id { get; set; }

    public ResourceVisualDescriptor Visual
    {
        get => _visual;
        set
        {
            if (_visual == value)
            {
                return;
            }

            _visual = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisualImageVisibility));
            OnPropertyChanged(nameof(GeneratedPreviewVisibility));
            OnPropertyChanged(nameof(FallbackVisibility));
            OnPropertyChanged(nameof(VisualStretch));
            OnPropertyChanged(nameof(IconSurfaceVisibility));
            OnPropertyChanged(nameof(VisualImageMargin));
        }
    }

    public ImageSource? VisualImage
    {
        get => _visualImage;
        private set
        {
            if (ReferenceEquals(_visualImage, value))
            {
                return;
            }

            _visualImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisualImageVisibility));
            OnPropertyChanged(nameof(FallbackVisibility));
            OnPropertyChanged(nameof(IconSurfaceVisibility));
        }
    }

    public bool IsVisualLoading
    {
        get => _isVisualLoading;
        set
        {
            if (_isVisualLoading == value)
            {
                return;
            }

            _isVisualLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisualLoadingVisibility));
        }
    }

    public Visibility VisualLoadingVisibility => IsVisualLoading ? Visibility.Visible : Visibility.Collapsed;

    public string VisualPreviewText
    {
        get => _visualPreviewText;
        private set
        {
            if (_visualPreviewText == value)
            {
                return;
            }

            _visualPreviewText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GeneratedPreviewVisibility));
            OnPropertyChanged(nameof(FallbackVisibility));
        }
    }

    public string VisualSourceLabel
    {
        get => _visualSourceLabel;
        private set
        {
            if (_visualSourceLabel == value)
            {
                return;
            }

            _visualSourceLabel = value;
            OnPropertyChanged();
        }
    }

    public Stretch VisualStretch => Visual.Kind == ResourceVisualKind.Icon ? Stretch.Uniform : Stretch.UniformToFill;

    public Thickness VisualImageMargin => Visual.Kind == ResourceVisualKind.Icon ? new Thickness(10) : new Thickness(0);

    public Thickness ListVisualImageMargin => Visual.Kind == ResourceVisualKind.Icon ? new Thickness(5) : new Thickness(0);

    public string SourceOrType
    {
        get
        {
            if (Uri.TryCreate(Address, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                return uri.Host;
            }

            var extension = Path.GetExtension(Address);
            return string.IsNullOrWhiteSpace(extension) ? Kind : extension.TrimStart('.').ToUpperInvariant();
        }
    }

    public string AccessibleName => $"{Title}, {Kind}";

    public string HoverDescription => string.IsNullOrWhiteSpace(Description) ? Title : $"{Title}\n{Description}";

    public GridLength CardPreviewHeight { get; private set; } = new(126);

    public int CardTitleLines { get; private set; } = 2;

    public int CardDescriptionLines { get; private set; } = 2;

    public Visibility CardTitleVisibility { get; private set; } = Visibility.Visible;

    public Visibility CardDescriptionVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility CardSourceVisibility { get; private set; } = Visibility.Visible;

    public Visibility CardDeleteVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility SourceColumnVisibility { get; private set; } = Visibility.Visible;

    public Visibility TagsColumnVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility DateColumnVisibility { get; private set; } = Visibility.Visible;

    public Visibility VisualImageVisibility => VisualImage is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IconSurfaceVisibility =>
        Visual.Kind == ResourceVisualKind.Icon && VisualImage is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility GeneratedPreviewVisibility =>
        Visual.Kind == ResourceVisualKind.GeneratedPreview && !string.IsNullOrWhiteSpace(VisualPreviewText)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility FallbackVisibility =>
        VisualImageVisibility == Visibility.Collapsed && GeneratedPreviewVisibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateCardPresentation(int size, bool showTitle, bool showDescription, bool showDelete)
    {
        CardPreviewHeight = new GridLength(size switch { 0 => 112, 2 => 178, _ => 126 });
        CardTitleLines = size == 0 ? 1 : 2;
        CardDescriptionLines = size switch { 0 => 1, 2 => 3, _ => 2 };
        CardTitleVisibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
        CardDescriptionVisibility = showDescription && !string.IsNullOrWhiteSpace(Description)
            ? Visibility.Visible : Visibility.Collapsed;
        CardSourceVisibility = size == 0 ? Visibility.Collapsed : Visibility.Visible;
        CardDeleteVisibility = showDelete ? Visibility.Visible : Visibility.Collapsed;
        OnPropertyChanged(nameof(CardPreviewHeight));
        OnPropertyChanged(nameof(CardTitleLines));
        OnPropertyChanged(nameof(CardDescriptionLines));
        OnPropertyChanged(nameof(CardTitleVisibility));
        OnPropertyChanged(nameof(CardDescriptionVisibility));
        OnPropertyChanged(nameof(CardSourceVisibility));
        OnPropertyChanged(nameof(CardDeleteVisibility));
        OnPropertyChanged(nameof(HoverDescription));
    }

    public void ApplyVisualResolution(ResourceVisualResolution resolution, ImageSource? imageSource)
    {
        Visual = resolution.Descriptor;
        VisualImage = imageSource;
        VisualPreviewText = resolution.PreviewText ?? string.Empty;
        VisualSourceLabel = resolution.SourceLabel;
    }

    public void UpdatePresentation(
        bool showSourceColumn,
        bool showTagsColumn,
        bool showDateColumn)
    {
        SourceColumnVisibility = showSourceColumn ? Visibility.Visible : Visibility.Collapsed;
        TagsColumnVisibility = showTagsColumn ? Visibility.Visible : Visibility.Collapsed;
        DateColumnVisibility = showDateColumn ? Visibility.Visible : Visibility.Collapsed;
        OnPropertyChanged(nameof(SourceColumnVisibility));
        OnPropertyChanged(nameof(TagsColumnVisibility));
        OnPropertyChanged(nameof(DateColumnVisibility));
        OnPropertyChanged(nameof(SourceOrType));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
