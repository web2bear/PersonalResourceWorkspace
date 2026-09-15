using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PersonalResourceWorkspace.Application.Abstractions;
using PersonalResourceWorkspace.Application.Collections;
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

namespace PersonalResourceWorkspace.App;

public sealed partial class MainPage : Page
{
    private readonly List<WorkspaceResource> _resources =
    [
        new("Проект Personal Space", "12 файлов и 3 заметки", "Папка", "Работа", "сегодня", "\uE8B7", Brush(109, 92, 231), true),
        new("Архитектура приложения.pdf", "PDF · 2,4 МБ", "Файл", "Работа", "вчера", "\uEA90", Brush(226, 90, 90), false),
        new("Заметки по WinUI.md", "Markdown · 18 КБ", "Файл", "Работа", "2 дня назад", "\uE70F", Brush(88, 157, 234), false),
        new("UI-референсы", "18 изображений", "Папка", "Дизайн", "сегодня", "\uE8B7", Brush(70, 184, 166), true),
        new("Палитра и типографика.fig", "Figma · 840 КБ", "Файл", "Дизайн", "3 дня назад", "\uE790", Brush(232, 132, 83), false),
        new("Наброски продуктов", "7 документов", "Папка", "Идеи", "вчера", "\uE8B7", Brush(225, 178, 62), false),
        new("Идеи автоматизации.md", "Markdown · 9 КБ", "Файл", "Идеи", "4 дня назад", "\uE70F", Brush(153, 126, 224), true),
        new("Building a Second Brain.pdf", "PDF · 6,1 МБ", "Файл", "Читать позже", "неделю назад", "\uEA90", Brush(177, 96, 194), false),
    ];

    private const string AllCollectionTag = "all";
    private const string FavoritesCollectionTag = "favorites";
    private const string AddCollectionTag = "add-collection";

    private static readonly string[] CollectionGlyphs =
    [
        "\uE8B7", "\uE8A5", "\uE71B", "\uE734", "\uE790", "\uEA80", "\uE8C3", "\uE896",
        "\uE8F1", "\uE8D6", "\uE714", "\uE715", "\uE8EC", "\uE8A1", "\uE9F9", "\uE90F",
        "\uE774", "\uE8B0", "\uE946", "\uE8F2", "\uE7F6", "\uE8D4", "\uE716", "\uE8FD",
    ];

    private static readonly string[] CollectionColors =
    [
        "#FF9B72", "#58C7B6", "#F4C95D", "#A995FF", "#7EA7FF",
        "#E25A5A", "#6D5CE7", "#46B8A6", "#E1B23E", "#B160C2",
    ];

    private readonly List<WorkspaceGroup> _collections = [];
    private string _activeCollection = AllCollectionTag;
    private readonly IResourceRepository _resourceRepository;
    private readonly ICollectionRepository _collectionRepository;
    private readonly ResourceLaunchService _resourceLaunchService;
    private readonly IClipboardService _clipboardService;
    private readonly IResourceVisualService _resourceVisualService;
    private readonly ICollectionViewPreferenceStore _viewPreferenceStore;
    private readonly Window _window;
    private CollectionViewMode _viewMode = CollectionViewMode.Grid;
    private ResourceSortMode _sortMode = ResourceSortMode.Title;
    private WorkspaceResource? _selectedResource;
    private bool _synchronizingSelection;
    private string? _lastDropSignature;
    private long _lastDropTicks;

    public ObservableCollection<WorkspaceResource> FilteredResources { get; } = [];

    public MainPage(
        Window window,
        IResourceRepository resourceRepository,
        ICollectionRepository collectionRepository,
        ResourceLaunchService resourceLaunchService,
        IClipboardService clipboardService,
        IResourceVisualService resourceVisualService,
        ICollectionViewPreferenceStore viewPreferenceStore)
    {
        _window = window;
        _resourceRepository = resourceRepository;
        _collectionRepository = collectionRepository;
        _resourceLaunchService = resourceLaunchService;
        _clipboardService = clipboardService;
        _resourceVisualService = resourceVisualService;
        _viewPreferenceStore = viewPreferenceStore;
        InitializeComponent();
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
        await RestoreViewModeAsync();
        await LoadCollectionsAsync();
        await LoadResourcesAsync();
        UpdateResponsiveLayout(ActualWidth);
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromArgb(255, red, green, blue));

    private static Brush ThemeBrush(string resourceKey, Brush fallback) =>
        Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
            ? brush
            : fallback;

    private void ShellNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            PageTitle.Text = "Настройки";
            StatusText.Text = "Раздел настроек будет добавлен позднее";
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case AllCollectionTag:
                _activeCollection = AllCollectionTag;
                PageTitle.Text = "Все ресурсы";
                ApplyFilter();
                break;
            case FavoritesCollectionTag:
                _activeCollection = FavoritesCollectionTag;
                PageTitle.Text = "Избранное";
                ApplyFilter();
                break;
            case AddCollectionTag:
                _ = AddCollectionAsync();
                break;
            case WorkspaceGroup collection:
                _activeCollection = collection.Name;
                PageTitle.Text = collection.Name;
                ApplyFilter();
                break;
        }
    }

    private async void AddCollection_Click(object sender, RoutedEventArgs e) => await AddCollectionAsync();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplyFilter();
        }
    }

    private async void AddResource_Click(object sender, RoutedEventArgs e)
    {
        var collection = GetDefaultCollection();
        var draft = await TryReadClipboardDraftAsync();
        var resource = draft is null
            ? CreateEmptyResource(collection)
            : ToWorkspaceResource(draft, collection);

        if (await ShowResourceEditorAsync(resource, isNew: true))
        {
            _resources.Insert(0, resource);
            ApplyFilter();
            await SaveResourcesAsync();
            StatusText.Text = $"Добавлено: {resource.Title}";
        }
    }

    private async Task<ResourceDraft?> TryReadClipboardDraftAsync()
    {
        try
        {
            var snapshot = await _clipboardService.ReadAsync(CancellationToken.None);
            return ResourceDraftFactory.TryCreate(snapshot);
        }
        catch (Exception)
        {
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
        (_viewMode == CollectionViewMode.Grid ? ResourcesGrid.SelectedItem : ResourcesList.SelectedItem)
        as WorkspaceResource;

    private async void GridViewToggle_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(CollectionViewMode.Grid, persist: true);

    private async void ListViewToggle_Click(object sender, RoutedEventArgs e) =>
        await SetViewModeAsync(CollectionViewMode.List, persist: true);

    private async Task RestoreViewModeAsync()
    {
        try
        {
            await SetViewModeAsync(await _viewPreferenceStore.LoadAsync(CancellationToken.None), persist: false);
        }
        catch (Exception exception)
        {
            await SetViewModeAsync(CollectionViewMode.Grid, persist: false);
            StatusText.Text = $"Не удалось восстановить вид: {exception.Message}";
        }
    }

    private async Task SetViewModeAsync(CollectionViewMode mode, bool persist)
    {
        _viewMode = mode;
        GridViewToggle.IsChecked = mode == CollectionViewMode.Grid;
        ListViewToggle.IsChecked = mode == CollectionViewMode.List;
        ResourcesGrid.Visibility = mode == CollectionViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        ResourcesList.Visibility = mode == CollectionViewMode.List ? Visibility.Visible : Visibility.Collapsed;

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
                StatusText.Text = $"Не удалось сохранить вид: {exception.Message}";
            }
        }
    }

    private void SortByTitle_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = ResourceSortMode.Title;
        SortByTitleItem.IsChecked = true;
        SortByModifiedItem.IsChecked = false;
        ApplyFilter();
    }

    private void SortByModified_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = ResourceSortMode.Modified;
        SortByTitleItem.IsChecked = false;
        SortByModifiedItem.IsChecked = true;
        ApplyFilter();
    }

    private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        var narrow = width < 720;
        WorkArea.Padding = narrow ? new Thickness(16, 14, 16, 16) : new Thickness(28, 20, 28, 22);
        DropHint.Visibility = width < 620 ? Visibility.Collapsed : Visibility.Visible;

        if (ResourcesGrid.ItemsPanelRoot is ItemsWrapGrid wrapGrid && ResourcesGrid.ActualWidth > 0)
        {
            var available = Math.Max(210, ResourcesGrid.ActualWidth - 4);
            if (available < 460)
            {
                wrapGrid.ItemWidth = available;
            }
            else
            {
                var columns = Math.Max(1, (int)Math.Floor((available + 12) / 222));
                wrapGrid.ItemWidth = Math.Clamp((available - ((columns - 1) * 12)) / columns, 210, 230);
            }
        }

        UpdateResourcePresentation(width);
    }

    private void WorkArea_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Добавить в Personal Space";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        DropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void WorkArea_DragLeave(object sender, DragEventArgs e)
    {
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
        var collection = GetDefaultCollection();
        var added = new List<WorkspaceResource>();
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                added.Add(ToWorkspaceResource(ResourceDraftFactory.FromPath(path), collection));
            }
        }

        await AddResourcesAsync(added);
    }

    private async Task<bool> AddDroppedResourcesAsync(DataPackageView dataView)
    {
        try
        {
            var collection = GetDefaultCollection();
            return await AddResourcesAsync(await CollectDroppedResourcesAsync(dataView, collection));
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось добавить: {exception.Message}";
            return false;
        }
    }

    private async Task<bool> AddResourcesAsync(List<WorkspaceResource> added)
    {
        if (IsDuplicateDrop(added))
        {
            return true;
        }

        foreach (var resource in added)
        {
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

    private static async Task<List<WorkspaceResource>> CollectDroppedResourcesAsync(
        DataPackageView dataView,
        string collection)
    {
        var added = new List<WorkspaceResource>();

        try
        {
            var items = await dataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                added.Add(CreateResourceFromStorageItem(item, collection));
            }

            if (added.Count > 0)
            {
                return added;
            }
        }
        catch (Exception)
        {
            // Unpackaged / island payloads often expose CF_HDROP without a pre-populated StorageItems format.
        }

        if (dataView.Contains(StandardDataFormats.Uri))
        {
            added.Add(ToWorkspaceResource(ResourceDraftFactory.FromUri(await dataView.GetUriAsync()), collection));
            return added;
        }

        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            added.Add(ToWorkspaceResource(ResourceDraftFactory.FromUri(await dataView.GetWebLinkAsync()), collection));
            return added;
        }

        if (dataView.Contains(StandardDataFormats.Text))
        {
            var text = (await dataView.GetTextAsync()).Trim();
            if (ResourceDraftFactory.TryParseText(text) is { } draft)
            {
                added.Add(ToWorkspaceResource(draft, collection));
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
        var collectionNames = _collections
            .Where(collection => !collection.IsTrash || collection.Name == resource.Collection)
            .Select(collection => collection.Name)
            .ToArray();
        var collectionBox = new ComboBox
        {
            Header = "Коллекция",
            ItemsSource = collectionNames.Length > 0 ? collectionNames : new[] { GetDefaultCollection() },
            SelectedItem = collectionNames.Contains(resource.Collection) ? resource.Collection : GetDefaultCollection(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var favoriteBox = new CheckBox
        {
            Content = "Добавить в избранное",
            IsChecked = resource.IsFavorite,
        };

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
        fields.Children.Add(kindBox);
        fields.Children.Add(addressBox);
        fields.Children.Add(addressHint);
        fields.Children.Add(visualModeBox);
        fields.Children.Add(previewSurface);
        fields.Children.Add(visualStatus);
        fields.Children.Add(visualButtons);
        fields.Children.Add(validationText);
        fields.Children.Add(collectionBox);
        fields.Children.Add(favoriteBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = isNew ? "Добавить ресурс" : "Редактировать ресурс",
            Content = fields,
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
        resource.Collection = collectionBox.SelectedItem as string ?? GetDefaultCollection();
        resource.IsFavorite = favoriteBox.IsChecked == true;
        resource.Glyph = ResourceTypeMapper.GlyphForStoredKind(resource.Kind);
        resource.AccentBrush = AccentFor(resource.Kind);
        resource.Visual = committedVisual;
        await SaveResourceAsync(resource);
        _ = ResolveVisualAsync(resource, forceRefresh: false);
        return true;
    }

    private async Task LoadCollectionsAsync()
    {
        try
        {
            var saved = await _collectionRepository.GetAllAsync(CancellationToken.None);
            _collections.Clear();
            _collections.AddRange(saved.Select(WorkspaceGroup.FromRecord));
            RebuildCollectionNavItems();
            SelectActiveNavItem();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось загрузить коллекции: {exception.Message}";
        }
    }

    private async Task LoadResourcesAsync()
    {
        try
        {
            var saved = await _resourceRepository.GetAllAsync(CancellationToken.None);
            _resources.Clear();
            _resources.AddRange(saved.Select(resource => new WorkspaceResource(
                resource.Title,
                resource.Description,
                ResourceTypeMapper.NormalizeStoredKind(resource.Kind),
                resource.Collection,
                resource.Modified,
                ResourceTypeMapper.GlyphForStoredKind(resource.Kind),
                AccentFor(resource.Kind),
                resource.IsFavorite,
                resource.Address,
                resource.Visual)
            { Id = resource.Id }));
            ApplyFilter();
            await ResolveVisualsAsync(_resources, forceRefresh: false);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось загрузить ресурсы: {exception.Message}";
        }
    }

    private async Task SaveResourceAsync(WorkspaceResource resource) =>
        await _resourceRepository.SaveAsync(
            new ResourceRecord(
                resource.Id,
                resource.Title,
                resource.Description,
                resource.Kind,
                resource.Collection,
                resource.Modified,
                resource.Glyph,
                resource.IsFavorite,
                resource.Address,
                resource.Visual),
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
            if (previousDescriptor != resolution.Descriptor)
            {
                await SaveResourceAsync(resource);
            }
        }
        catch (Exception)
        {
            resource.ApplyVisualResolution(
                new ResourceVisualResolution(
                    resource.Visual,
                    null,
                    null,
                    "Значок типа"),
                null);
        }
    }

    private static async Task<ImageSource> LoadImageSourceAsync(string path, CancellationToken cancellationToken)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
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
            StatusText.Text = $"Открывается: {address.Trim()}";
        }
        catch (Exception exception)
        {
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Не удалось открыть ресурс",
                Content = exception.Message,
                CloseButtonText = "Закрыть",
            };
            await errorDialog.ShowAsync();
        }
    }

    private string GetDefaultCollection()
    {
        if (_collections.FirstOrDefault(collection => collection.Name == _activeCollection) is { IsTrash: false } active)
        {
            return active.Name;
        }

        return _collections.FirstOrDefault(collection => collection.IsDefault)?.Name
            ?? _collections.FirstOrDefault(collection => !collection.IsTrash)?.Name
            ?? "Входящие";
    }

    private WorkspaceGroup? GetTrashCollection() =>
        _collections.FirstOrDefault(collection => collection.IsTrash);

    private bool IsInTrash(WorkspaceResource resource) =>
        GetTrashCollection() is { } trash &&
        string.Equals(resource.Collection, trash.Name, StringComparison.OrdinalIgnoreCase);

    private static WorkspaceResource CreateEmptyResource(string collection) =>
        ToWorkspaceResource(
            new ResourceDraft(
                string.Empty,
                string.Empty,
                StoredResourceKinds.File,
                string.Empty),
            collection);

    private static WorkspaceResource ToWorkspaceResource(ResourceDraft draft, string collection) =>
        new(
            draft.Title,
            draft.Description,
            draft.Kind,
            collection,
            "только что",
            ResourceTypeMapper.GlyphForStoredKind(draft.Kind),
            AccentFor(draft.Kind),
            false,
            draft.Address);

    private static WorkspaceResource CreateResourceFromStorageItem(IStorageItem item, string collection)
    {
        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            return ToWorkspaceResource(ResourceDraftFactory.FromPath(item.Path), collection);
        }

        var kind = item is StorageFolder ? StoredResourceKinds.Folder : StoredResourceKinds.File;
        return ToWorkspaceResource(
            new ResourceDraft(item.Name, string.Empty, kind, item.Path),
            collection);
    }

    private void ApplyFilter()
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        IEnumerable<WorkspaceResource> matches = _resources.Where(resource =>
            ((_activeCollection == AllCollectionTag && !IsInTrash(resource)) ||
             (_activeCollection == FavoritesCollectionTag && resource.IsFavorite && !IsInTrash(resource)) ||
             resource.Collection == _activeCollection) &&
            (query.Length == 0 ||
             resource.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             resource.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
              resource.Address.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
              resource.Kind.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

        matches = _sortMode == ResourceSortMode.Modified
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

        UpdateCollectionCounts();
        UpdateResourcePresentation(ActualWidth);
    }

    private static string FormatResourceCount(int count) => count switch
    {
        1 => "1 ресурс",
        >= 2 and <= 4 => $"{count} ресурса",
        _ => $"{count} ресурсов",
    };

    private void UpdateCollectionCounts()
    {
        AllResourcesCount.Text = _resources.Count(resource => !IsInTrash(resource)).ToString(CultureInfo.CurrentCulture);
        FavoritesCount.Text = _resources.Count(resource => resource.IsFavorite && !IsInTrash(resource)).ToString(CultureInfo.CurrentCulture);
        foreach (var item in ShellNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is WorkspaceGroup collection)
            {
                var count = _resources.Count(resource => resource.Collection == collection.Name);
                item.Content = CreateNavigationContent(collection.Name, count);
            }
        }
    }

    private void UpdateResourcePresentation(double width)
    {
        var showCollectionContext = _activeCollection is AllCollectionTag or FavoritesCollectionTag ||
            !string.IsNullOrWhiteSpace(SearchBox?.Text);
        foreach (var resource in _resources)
        {
            resource.UpdatePresentation(
                showCollectionContext,
                showSourceColumn: width >= 660,
                showCollectionColumn: showCollectionContext && width >= 850,
                showDateColumn: width >= 1020);
        }
    }

    private async Task AddCollectionAsync()
    {
        try
        {
            var collection = new WorkspaceGroup
            {
                Id = Guid.NewGuid(),
                Name = string.Empty,
                Glyph = "\uE8B7",
                Color = "#7EA7FF",
                SortOrder = _collections.Count == 0 ? 0 : _collections.Max(item => item.SortOrder) + 1,
                IsDefault = _collections.Count == 0,
                IsTrash = false,
            };

            if (!await ShowCollectionDialogAsync(collection, "Новая коллекция", "Создать", editName: true, editIcon: true))
            {
                SelectActiveNavItem();
                return;
            }

            await _collectionRepository.SaveAsync(collection.ToRecord(), CancellationToken.None);
            _collections.Add(collection);
            _activeCollection = collection.Name;
            PageTitle.Text = collection.Name;
            RebuildCollectionNavItems();
            ApplyFilter();
            SelectActiveNavItem();
            StatusText.Text = $"Создана коллекция: {collection.Name}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось создать коллекцию: {exception.Message}";
            SelectActiveNavItem();
        }
    }

    private async void RenameCollectionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceGroup collection })
        {
            await UpdateCollectionAsync(collection, "Переименовать коллекцию", "Сохранить", editName: true, editIcon: false);
        }
    }

    private async void ChangeCollectionIconMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceGroup collection })
        {
            await UpdateCollectionAsync(collection, "Иконка коллекции", "Сохранить", editName: false, editIcon: true);
        }
    }

    private async void DeleteCollectionMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceGroup collection })
        {
            await DeleteCollectionAsync(collection);
        }
    }

    private async void EmptyTrashMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: WorkspaceGroup collection } && collection.IsTrash)
        {
            await EmptyTrashAsync(collection);
        }
    }

    private async Task DeleteResourceAsync(WorkspaceResource resource)
    {
        try
        {
            var trash = GetTrashCollection();
            if (trash is null)
            {
                await ShowMessageAsync("Корзина недоступна", "Системная коллекция «Корзина» не найдена.");
                return;
            }

            var isAlreadyInTrash = IsInTrash(resource);
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = isAlreadyInTrash ? "Удалить ресурс безвозвратно?" : "Удалить ресурс?",
                Content = isAlreadyInTrash
                    ? $"Ресурс «{resource.Title}» будет удалён полностью и его нельзя будет восстановить."
                    : $"Ресурс «{resource.Title}» будет перемещён в «{trash.Name}».",
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
                await _resourceRepository.DeleteAsync(resource.Id, CancellationToken.None);
                _resources.Remove(resource);
                ApplyFilter();
                StatusText.Text = $"Удалено безвозвратно: {resource.Title}";
                return;
            }

            resource.Collection = trash.Name;
            await SaveResourceAsync(resource);
            ApplyFilter();
            StatusText.Text = $"Перемещено в корзину: {resource.Title}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось удалить ресурс: {exception.Message}";
        }
    }

    private async Task EmptyTrashAsync(WorkspaceGroup trash)
    {
        try
        {
            var trashed = _resources
                .Where(resource => string.Equals(resource.Collection, trash.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (trashed.Count == 0)
            {
                await ShowMessageAsync("Корзина пуста", "В корзине нет ресурсов.");
                return;
            }

            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Очистить корзину?",
                Content = $"Все ресурсы в корзине ({trashed.Count}) будут удалены полностью и их нельзя будет восстановить.",
                PrimaryButtonText = "Очистить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _resourceRepository.DeleteByCollectionAsync(trash.Name, CancellationToken.None);
            foreach (var resource in trashed)
            {
                _resources.Remove(resource);
            }

            ApplyFilter();
            StatusText.Text = "Корзина очищена";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось очистить корзину: {exception.Message}";
        }
    }

    private async Task UpdateCollectionAsync(
        WorkspaceGroup collection,
        string title,
        string primaryButtonText,
        bool editName,
        bool editIcon)
    {
        try
        {
            var previousName = collection.Name;
            if (!await ShowCollectionDialogAsync(collection, title, primaryButtonText, editName, editIcon))
            {
                return;
            }

            await _collectionRepository.SaveAsync(collection.ToRecord(), CancellationToken.None);
            if (!string.Equals(previousName, collection.Name, StringComparison.Ordinal))
            {
                foreach (var resource in _resources.Where(resource => resource.Collection == previousName))
                {
                    resource.Collection = collection.Name;
                }

                if (_activeCollection == previousName)
                {
                    _activeCollection = collection.Name;
                    PageTitle.Text = collection.Name;
                }
            }

            RebuildCollectionNavItems();
            ApplyFilter();
            SelectActiveNavItem();
            StatusText.Text = $"Сохранена коллекция: {collection.Name}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось сохранить коллекцию: {exception.Message}";
        }
    }

    private async Task DeleteCollectionAsync(WorkspaceGroup collection)
    {
        try
        {
            if (collection.IsDefault)
            {
                await ShowMessageAsync("Нельзя удалить", "Коллекцию по умолчанию удалить нельзя.");
                return;
            }

            if (collection.IsTrash)
            {
                await ShowMessageAsync("Нельзя удалить", "Корзину удалить нельзя.");
                return;
            }

            var fallback = _collections.FirstOrDefault(item => item.IsDefault && !item.IsTrash && item.Id != collection.Id)
                ?? _collections.FirstOrDefault(item => !item.IsTrash && item.Id != collection.Id);
            if (fallback is null)
            {
                await ShowMessageAsync("Нельзя удалить", "Должна остаться хотя бы одна коллекция.");
                return;
            }

            var resourceCount = _resources.Count(resource => resource.Collection == collection.Name);
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Удалить коллекцию?",
                Content = resourceCount == 0
                    ? $"Коллекция «{collection.Name}» будет удалена."
                    : $"Коллекция «{collection.Name}» будет удалена. Ресурсы ({resourceCount}) перейдут в «{fallback.Name}».",
                PrimaryButtonText = "Удалить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _collectionRepository.DeleteAsync(collection.Id, fallback.Id, CancellationToken.None);
            foreach (var resource in _resources.Where(resource => resource.Collection == collection.Name))
            {
                resource.Collection = fallback.Name;
            }

            _collections.Remove(collection);
            if (_activeCollection == collection.Name)
            {
                _activeCollection = fallback.Name;
                PageTitle.Text = fallback.Name;
            }

            RebuildCollectionNavItems();
            ApplyFilter();
            SelectActiveNavItem();
            StatusText.Text = $"Удалена коллекция: {collection.Name}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось удалить коллекцию: {exception.Message}";
        }
    }

    private async Task<bool> ShowCollectionDialogAsync(
        WorkspaceGroup collection,
        string title,
        string primaryButtonText,
        bool editName,
        bool editIcon)
    {
        var nameBox = new TextBox
        {
            Header = "Название",
            Text = collection.Name,
            PlaceholderText = "Например, Проекты",
            Visibility = editName ? Visibility.Visible : Visibility.Collapsed,
        };
        var selectedGlyph = collection.Glyph;
        var selectedColor = collection.Color;
        var previewGlyph = new FontIcon
        {
            Glyph = selectedGlyph,
            FontSize = 24,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
        };
        var previewImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Visibility = string.IsNullOrWhiteSpace(collection.VisualAssetPath) ? Visibility.Collapsed : Visibility.Visible,
        };
        var existingCollectionVisualPath = _resourceVisualService.GetAbsoluteAssetPath(collection.VisualAssetPath);
        if (existingCollectionVisualPath is not null && File.Exists(existingCollectionVisualPath))
        {
            try
            {
                previewImage.Source = await LoadImageSourceAsync(existingCollectionVisualPath, CancellationToken.None);
                previewGlyph.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                previewImage.Visibility = Visibility.Collapsed;
            }
        }

        var preview = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(12),
            Background = BrushFromHex(selectedColor),
            Child = new Grid { Children = { previewGlyph, previewImage } },
        };
        string? pendingCollectionVisualPath = null;
        var resetCollectionVisual = false;

        var accentBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"];
        var transparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        const int glyphColumns = 8;
        var glyphPanel = new Grid();
        AutomationProperties.SetName(glyphPanel, "Иконка коллекции");
        for (var column = 0; column < glyphColumns; column++)
        {
            glyphPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        Button? activeGlyphButton = null;
        for (var index = 0; index < CollectionGlyphs.Length; index++)
        {
            if (index % glyphColumns == 0)
            {
                glyphPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var glyph = CollectionGlyphs[index];
            var button = new Button
            {
                Tag = glyph,
                Width = 44,
                Height = 44,
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                Content = new FontIcon { Glyph = glyph, FontSize = 18 },
                BorderThickness = new Thickness(2),
                BorderBrush = glyph == selectedGlyph ? accentBrush : transparentBrush,
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            };
            AutomationProperties.SetName(button, "Выбрать иконку");
            if (glyph == selectedGlyph)
            {
                activeGlyphButton = button;
            }

            button.Click += (_, _) =>
            {
                selectedGlyph = glyph;
                previewGlyph.Glyph = glyph;
                previewGlyph.Visibility = Visibility.Visible;
                previewImage.Visibility = Visibility.Collapsed;
                pendingCollectionVisualPath = null;
                resetCollectionVisual = true;
                if (activeGlyphButton is not null)
                {
                    activeGlyphButton.BorderBrush = transparentBrush;
                }

                activeGlyphButton = button;
                button.BorderBrush = accentBrush;
            };

            Grid.SetRow(button, index / glyphColumns);
            Grid.SetColumn(button, index % glyphColumns);
            glyphPanel.Children.Add(button);
        }

        var colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var color in CollectionColors)
        {
            var swatch = new Button
            {
                Tag = color,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = BrushFromHex(color),
                BorderBrush = color == selectedColor
                    ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
                    : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(14),
            };
            swatch.Click += (_, _) =>
            {
                selectedColor = color;
                preview.Background = BrushFromHex(color);
                foreach (var child in colorPanel.Children.OfType<Button>())
                {
                    child.BorderBrush = child.Tag as string == selectedColor
                        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
                        : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                }
            };
            colorPanel.Children.Add(swatch);
        }

        var fields = new StackPanel { Spacing = 14, MinWidth = 420 };
        if (editName)
        {
            fields.Children.Add(nameBox);
        }

        if (editIcon)
        {
            fields.Children.Add(new TextBlock { Text = "Визуал коллекции", Opacity = 0.75 });
            fields.Children.Add(preview);
            var visualActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var chooseImageButton = new Button { Content = "Выбрать изображение…" };
            var useGlyphButton = new Button { Content = "Использовать значок" };
            chooseImageButton.Click += async (_, _) =>
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
                    try
                    {
                        previewImage.Source = await LoadImageSourceAsync(file.Path, CancellationToken.None);
                        previewImage.Visibility = Visibility.Visible;
                        previewGlyph.Visibility = Visibility.Collapsed;
                        pendingCollectionVisualPath = file.Path;
                        resetCollectionVisual = false;
                    }
                    catch (Exception exception)
                    {
                        await ShowMessageAsync("Не удалось показать изображение", exception.Message);
                    }
                }
            };
            useGlyphButton.Click += (_, _) =>
            {
                previewImage.Source = null;
                previewImage.Visibility = Visibility.Collapsed;
                previewGlyph.Visibility = Visibility.Visible;
                pendingCollectionVisualPath = null;
                resetCollectionVisual = true;
            };
            visualActions.Children.Add(chooseImageButton);
            visualActions.Children.Add(useGlyphButton);
            fields.Children.Add(visualActions);
            fields.Children.Add(new TextBlock { Text = "Значок типа", Opacity = 0.75 });
            fields.Children.Add(glyphPanel);
            fields.Children.Add(new TextBlock { Text = "Цвет", Opacity = 0.75 });
            fields.Children.Add(colorPanel);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = fields,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        var name = editName ? nameBox.Text.Trim() : collection.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            await ShowMessageAsync("Нужно название", "Введите название коллекции.");
            return false;
        }

        if (!collection.IsTrash &&
            string.Equals(name, SystemCollections.TrashName, StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("Имя занято", "Название «Корзина» зарезервировано.");
            return false;
        }

        if (_collections.Any(item =>
                item.Id != collection.Id &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            await ShowMessageAsync("Имя занято", "Коллекция с таким названием уже есть.");
            return false;
        }

        collection.Name = name;
        if (editIcon)
        {
            collection.Glyph = selectedGlyph;
            collection.Color = selectedColor;
            if (pendingCollectionVisualPath is not null)
            {
                try
                {
                    collection.VisualAssetPath = (await _resourceVisualService.ImportUserAssetAsync(
                        pendingCollectionVisualPath,
                        CancellationToken.None)).AssetPath;
                }
                catch (Exception exception)
                {
                    await ShowMessageAsync("Не удалось сохранить изображение", exception.Message);
                    return false;
                }
            }
            else if (resetCollectionVisual)
            {
                collection.VisualAssetPath = null;
            }
        }

        return true;
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

    private void RebuildCollectionNavItems()
    {
        var items = ShellNavigation.MenuItems;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (items[index] is NavigationViewItem nav &&
                (nav.Tag is WorkspaceGroup || Equals(nav.Tag, AddCollectionTag)))
            {
                items.RemoveAt(index);
            }
        }

        var insertAt = items.Count;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is NavigationViewItemHeader { Content: "Коллекции" })
            {
                insertAt = index + 1;
                break;
            }
        }

        foreach (var collection in _collections
            .OrderBy(item => item.IsTrash)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Name))
        {
            items.Insert(insertAt++, CreateCollectionNavItem(collection));
        }

        items.Insert(insertAt, CreateAddCollectionNavItem());
    }

    private NavigationViewItem CreateCollectionNavItem(WorkspaceGroup collection)
    {
        var item = new NavigationViewItem
        {
            Content = CreateNavigationContent(
                collection.Name,
                _resources.Count(resource => resource.Collection == collection.Name)),
            Tag = collection,
            Icon = CreateCollectionIcon(collection),
        };

        var flyout = new MenuFlyout();
        if (collection.IsTrash)
        {
            flyout.Items.Add(CreateCollectionMenuItem("Очистить корзину", new SymbolIcon(Symbol.Delete), collection, EmptyTrashMenu_Click));
        }
        else
        {
            flyout.Items.Add(CreateCollectionMenuItem("Переименовать", new SymbolIcon(Symbol.Rename), collection, RenameCollectionMenu_Click));
            flyout.Items.Add(CreateCollectionMenuItem("Изменить иконку", new FontIcon { Glyph = "\uE790" }, collection, ChangeCollectionIconMenu_Click));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(CreateCollectionMenuItem("Удалить", new SymbolIcon(Symbol.Delete), collection, DeleteCollectionMenu_Click));
        }

        item.ContextFlyout = flyout;
        return item;
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

    private IconElement CreateCollectionIcon(WorkspaceGroup collection)
    {
        var path = _resourceVisualService.GetAbsoluteAssetPath(collection.VisualAssetPath);
        if (path is not null && File.Exists(path))
        {
            try
            {
                return new ImageIcon
                {
                    Source = new BitmapImage(new Uri(path, UriKind.Absolute)),
                };
            }
            catch (Exception)
            {
            }
        }

        return new FontIcon
        {
            Glyph = collection.Glyph,
            Foreground = BrushFromHex(collection.Color),
        };
    }

    private static MenuFlyoutItem CreateCollectionMenuItem(
        string text,
        IconElement icon,
        WorkspaceGroup collection,
        RoutedEventHandler click)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = icon,
            Tag = collection,
        };
        item.Click += click;
        AutomationProperties.SetName(item, text);
        return item;
    }

    private static NavigationViewItem CreateAddCollectionNavItem()
    {
        var item = new NavigationViewItem
        {
            Content = "Новая коллекция",
            Tag = AddCollectionTag,
            SelectsOnInvoked = false,
            Icon = new FontIcon { Glyph = "\uE710" },
        };
        AutomationProperties.SetName(item, "Новая коллекция");
        return item;
    }

    private void SelectActiveNavItem()
    {
        foreach (var item in ShellNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            var isMatch = _activeCollection switch
            {
                AllCollectionTag => Equals(item.Tag, AllCollectionTag),
                FavoritesCollectionTag => Equals(item.Tag, FavoritesCollectionTag),
                _ => item.Tag is WorkspaceGroup collection && collection.Name == _activeCollection,
            };
            if (isMatch)
            {
                ShellNavigation.SelectedItem = item;
                return;
            }
        }
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
            case ResourceType.Collection:
                return Brush(225, 178, 62);
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
    }
}

public sealed class WorkspaceResource : INotifyPropertyChanged
{
    public WorkspaceResource(
        string title,
        string description,
        string kind,
        string collection,
        string modified,
        string glyph,
        SolidColorBrush accentBrush,
        bool isFavorite,
        string address = "",
        ResourceVisualDescriptor? visual = null)
    {
        Id = Guid.NewGuid();
        Title = title;
        Description = description;
        Kind = kind;
        Collection = collection;
        Modified = modified;
        Glyph = glyph;
        AccentBrush = accentBrush;
        IsFavorite = isFavorite;
        Address = address;
        _visual = visual ?? ResourceVisualDescriptor.Auto;
    }

    private ResourceVisualDescriptor _visual;
    private ImageSource? _visualImage;
    private string _visualPreviewText = string.Empty;
    private string _visualSourceLabel = "Значок типа";

    public string Title { get; set; }
    public string Description { get; set; }
    public string Kind { get; set; }
    public string Collection { get; set; }
    public string Modified { get; set; }
    public string Glyph { get; set; }
    public SolidColorBrush AccentBrush { get; set; }
    public bool IsFavorite { get; set; }
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

    public Visibility CollectionContextVisibility { get; private set; } = Visibility.Collapsed;

    public Visibility SourceColumnVisibility { get; private set; } = Visibility.Visible;

    public Visibility CollectionColumnVisibility { get; private set; } = Visibility.Collapsed;

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

    public void ApplyVisualResolution(ResourceVisualResolution resolution, ImageSource? imageSource)
    {
        Visual = resolution.Descriptor;
        VisualImage = imageSource;
        VisualPreviewText = resolution.PreviewText ?? string.Empty;
        VisualSourceLabel = resolution.SourceLabel;
    }

    public void UpdatePresentation(
        bool showCollectionContext,
        bool showSourceColumn,
        bool showCollectionColumn,
        bool showDateColumn)
    {
        CollectionContextVisibility = showCollectionContext ? Visibility.Visible : Visibility.Collapsed;
        SourceColumnVisibility = showSourceColumn ? Visibility.Visible : Visibility.Collapsed;
        CollectionColumnVisibility = showCollectionColumn ? Visibility.Visible : Visibility.Collapsed;
        DateColumnVisibility = showDateColumn ? Visibility.Visible : Visibility.Collapsed;
        OnPropertyChanged(nameof(CollectionContextVisibility));
        OnPropertyChanged(nameof(SourceColumnVisibility));
        OnPropertyChanged(nameof(CollectionColumnVisibility));
        OnPropertyChanged(nameof(DateColumnVisibility));
        OnPropertyChanged(nameof(SourceOrType));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class WorkspaceGroup
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Glyph { get; set; } = "\uE8B7";

    public string Color { get; set; } = "#7EA7FF";

    public int SortOrder { get; set; }

    public bool IsDefault { get; set; }

    public bool IsTrash { get; set; }

    public string? VisualAssetPath { get; set; }

    public static WorkspaceGroup FromRecord(CollectionRecord record) => new()
    {
        Id = record.Id,
        Name = record.Name,
        Glyph = record.Glyph,
        Color = record.Color,
        SortOrder = record.SortOrder,
        IsDefault = record.IsDefault,
        IsTrash = record.IsTrash,
        VisualAssetPath = record.VisualAssetPath,
    };

    public CollectionRecord ToRecord() => new(Id, Name, Glyph, Color, SortOrder, IsDefault, IsTrash, VisualAssetPath);
}

internal sealed record PersistedResource(
    string Title,
    string Description,
    string Kind,
    string Collection,
    string Modified,
    string Glyph,
    bool IsFavorite,
    string Address)
{
    public static PersistedResource FromModel(WorkspaceResource resource) => new(
        resource.Title,
        resource.Description,
        resource.Kind,
        resource.Collection,
        resource.Modified,
        resource.Glyph,
        resource.IsFavorite,
        resource.Address);

    public WorkspaceResource ToModel() => new(
        Title,
        Description,
        Kind,
        Collection,
        Modified,
        Glyph,
        new SolidColorBrush(Color.FromArgb(255, 88, 157, 234)),
        IsFavorite,
        Address);
}
