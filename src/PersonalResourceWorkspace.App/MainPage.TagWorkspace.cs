using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace PersonalResourceWorkspace.App;

public sealed partial class MainPage
{
    private readonly Dictionary<Guid, NavigationViewItem> _searchNavItems = [];
    private NavigationViewItem? _rootDropItem;

    private IReadOnlyList<Guid> ActiveRequiredTags() =>
        _searches.FirstOrDefault(search => search.Id == _activeSearchId)?.RequiredTagIds ?? [];

    private Guid[] NewResourceTags()
    {
        var tags = ActiveRequiredTags().Concat(_temporaryTags).ToHashSet();
        if (tags.Count == 0) tags.Add(SystemTags.InboxId);
        return tags.ToArray();
    }

    private string TagName(Guid id) => _tags.FirstOrDefault(tag => tag.Id == id)?.Name ?? id.ToString();

    private async Task LoadWorkspaceNavAsync()
    {
        try
        {
            _tags.Clear();
            _tags.AddRange(await _workspaceRepository.GetTagsAsync(CancellationToken.None));
            _searches.Clear();
            _searches.AddRange(await _workspaceRepository.GetSearchesAsync(CancellationToken.None));
            RebuildSearchNavItems();
            UpdateTagFilterChips();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось загрузить тэги и поиски: {exception.Message}";
        }
    }

    private void RebuildSearchNavItems()
    {
        for (var index = ShellNavigation.MenuItems.Count - 1; index >= 0; index--)
        {
            if (ShellNavigation.MenuItems[index] is NavigationViewItem item && item.Tag is SavedSearchRecord)
                ShellNavigation.MenuItems.RemoveAt(index);
        }
        _searchNavItems.Clear();
        if (_rootDropItem is not null) ShellNavigation.MenuItems.Remove(_rootDropItem);
        foreach (var search in _searches)
        {
            var item = CreateSearchNavItem(search);
            _searchNavItems.Add(search.Id, item);
        }
        foreach (var search in _searches.OrderBy(item => item.SortOrder))
        {
            if (search.ParentId is { } parentId && _searchNavItems.TryGetValue(parentId, out var parent))
                parent.MenuItems.Add(_searchNavItems[search.Id]);
            else
                ShellNavigation.MenuItems.Add(_searchNavItems[search.Id]);
        }
        _rootDropItem = new NavigationViewItem
        {
            Content = "Перетащите сюда для верхнего уровня",
            Icon = new SymbolIcon(Symbol.Up),
            AllowDrop = true,
            SelectsOnInvoked = false,
        };
        AutomationProperties.SetName(_rootDropItem, "Переместить поиск на верхний уровень");
        _rootDropItem.DragOver += (_, args) =>
        {
            args.AcceptedOperation = TryGetDraggedSearch(args.DataView.Properties, out var id) &&
                _searches.Any(search => search.Id == id && !search.IsSystem)
                ? DataPackageOperation.Move : DataPackageOperation.None;
            args.Handled = true;
        };
        _rootDropItem.Drop += async (_, args) =>
        {
            args.Handled = true;
            if (TryGetDraggedSearch(args.DataView.Properties, out var id))
                await MoveSearchToIndexAsync(id, null, _searches.Count(search => search.ParentId is null));
        };
        ShellNavigation.MenuItems.Add(_rootDropItem);
        ShellNavigation.SelectedItem = _activeSearchId is { } active && _searchNavItems.TryGetValue(active, out var selected)
            ? selected : AllResourcesNavItem;
        UpdateSearchCounts();
    }

    private NavigationViewItem CreateSearchNavItem(SavedSearchRecord search)
    {
        var item = new NavigationViewItem
        {
            Tag = search,
            Content = CreateSearchDropContent(search.Name, 0),
            Icon = new FontIcon { Glyph = search.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = BrushFromHex(search.Color) },
            AllowDrop = true,
            CanDrag = !search.IsSystem,
        };
        AutomationProperties.SetName(item, search.Name);
        item.DragStarting += (_, args) =>
        {
            args.Data.Properties["SearchId"] = search.Id.ToString();
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        AttachSearchDragGesture(item, search);
        item.DragOver += (_, args) =>
        {
            if (args.DataView.Properties.ContainsKey("ResourceId"))
                args.AcceptedOperation = CanDropResourceOn(search) ? DataPackageOperation.Copy : DataPackageOperation.None;
            else if (TryGetDraggedSearch(args.DataView.Properties, out var sourceId))
            {
                var source = _searches.FirstOrDefault(candidate => candidate.Id == sourceId);
                var valid = source is not null && !source.IsSystem && sourceId != search.Id &&
                    !IsDescendant(search, sourceId);
                args.AcceptedOperation = valid ? DataPackageOperation.Move : DataPackageOperation.None;
                ShowSearchDropZone(item, valid ? SearchDropPosition(args.GetPosition(item).Y, item.ActualHeight) : -1);
            }
            args.Handled = true;
        };
        item.DragLeave += (_, _) => ShowSearchDropZone(item, -1);
        item.Drop += async (_, args) =>
        {
            args.Handled = true;
            ShowSearchDropZone(item, -1);
            if (args.DataView.Properties.TryGetValue("ResourceId", out var resourceValue) &&
                Guid.TryParse(resourceValue as string, out var resourceId))
            {
                var resource = _resources.FirstOrDefault(candidate => candidate.Id == resourceId);
                if (resource is not null) await AssignSearchTagsByDropAsync(resource, search);
            }
            else if (args.DataView.Properties.TryGetValue("SearchId", out var searchValue) &&
                     Guid.TryParse(searchValue as string, out var sourceId))
            {
                var position = SearchDropPosition(args.GetPosition(item).Y, item.ActualHeight);
                if (position == 0)
                    await MoveSearchRelativeAsync(sourceId, search, before: true);
                else if (position == 2)
                    await MoveSearchRelativeAsync(sourceId, search, before: false);
                else
                    await MoveSearchToIndexAsync(sourceId, search.Id, 0);
            }
        };
        var flyout = new MenuFlyout();
        AddSearchMenuItem(flyout, "Редактировать поиск", async () => await EditSearchAsync(search));
        AddSearchMenuItem(flyout, "Переместить выше", async () => await ShiftSearchAsync(search, -1));
        AddSearchMenuItem(flyout, "Переместить ниже", async () => await ShiftSearchAsync(search, 1));
        if (!search.IsSystem)
        {
            AddSearchMenuItem(flyout, "Переместить в…", async () => await ChooseSearchParentAsync(search));
            AddSearchMenuItem(flyout, "Переместить на верхний уровень", async () => await MoveSearchAsync(search.Id, null, null));
            AddSearchMenuItem(flyout, "Удалить поиск", async () => await DeleteSearchAsync(search));
        }
        if (search.Id == SystemTags.TrashSearchId)
            AddSearchMenuItem(flyout, "Очистить корзину", EmptyTagTrashAsync);
        item.ContextFlyout = flyout;
        return item;
    }

    private void AttachSearchDragGesture(NavigationViewItem item, SavedSearchRecord search)
    {
        if (search.IsSystem) return;
        global::Windows.Foundation.Point? pressedAt = null;
        var dragStarted = false;
        item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            var point = args.GetCurrentPoint(item);
            pressedAt = point.Properties.IsLeftButtonPressed ? point.Position : null;
            dragStarted = false;
        }), true);
        item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pressedAt = null), true);
        item.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => pressedAt = null), true);
        item.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, args) =>
        {
            if (pressedAt is not { } start || dragStarted) return;
            var point = args.GetCurrentPoint(item);
            if (!point.Properties.IsLeftButtonPressed)
            {
                pressedAt = null;
                return;
            }
            if (Math.Abs(point.Position.X - start.X) < 8 && Math.Abs(point.Position.Y - start.Y) < 8) return;
            dragStarted = true;
            pressedAt = null;
            try
            {
                await item.StartDragAsync(point);
            }
            catch (Exception exception)
            {
                StatusText.Text = $"Не удалось начать перетаскивание: {exception.Message}";
            }
        }), true);
    }

    private static bool TryGetDraggedSearch(DataPackagePropertySetView properties, out Guid id)
    {
        id = default;
        return properties.TryGetValue("SearchId", out var value) && Guid.TryParse(value as string, out id);
    }

    private static int SearchDropPosition(double y, double height) =>
        y < height * 0.25 ? 0 : y > height * 0.75 ? 2 : 1;

    private static Grid CreateSearchDropContent(string name, int count)
    {
        var content = CreateNavigationContent(name, count);
        var grid = new Grid();
        grid.Children.Add(content);
        grid.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 76, 156, 255)), Visibility = Visibility.Collapsed });
        grid.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 76, 156, 255)), Visibility = Visibility.Collapsed });
        return grid;
    }

    private static void ShowSearchDropZone(NavigationViewItem item, int position)
    {
        if (item.Content is not Grid grid || grid.Children.Count < 3) return;
        grid.Children[1].Visibility = position == 0 ? Visibility.Visible : Visibility.Collapsed;
        grid.Children[2].Visibility = position == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AddSearchMenuItem(MenuFlyout flyout, string title, Func<Task> action)
    {
        var menu = new MenuFlyoutItem { Text = title };
        menu.Click += async (_, _) => await action();
        flyout.Items.Add(menu);
    }

    private static bool CanDropResourceOn(SavedSearchRecord search) =>
        search.RequiredTagIds.Count > 0 &&
        (!search.RequiredTagIds.Contains(SystemTags.TrashId) || search.Id == SystemTags.TrashSearchId);

    private async Task AssignSearchTagsByDropAsync(WorkspaceResource resource, SavedSearchRecord search)
    {
        if (!CanDropResourceOn(search)) return;
        if (search.Id == SystemTags.TrashSearchId)
        {
            await DeleteResourceAsync(resource);
            return;
        }
        var added = search.RequiredTagIds.Where(id => !resource.TagIds.Contains(id)).ToArray();
        if (added.Length == 0)
        {
            StatusText.Text = "Все тэги поиска уже назначены";
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Назначить тэги ресурсу?",
            Content = $"«{resource.Title}»: {string.Join(", ", added.Select(TagName))}",
            PrimaryButtonText = "Назначить",
            CloseButtonText = "Отмена",
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        resource.TagIds.UnionWith(added);
        await SaveResourceAsync(resource);
        ApplyFilter();
    }

    private void Resources_DragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (args.Items.FirstOrDefault() is WorkspaceResource resource)
            args.Data.Properties["ResourceId"] = resource.Id.ToString();
    }

    private async Task ShiftSearchAsync(SavedSearchRecord search, int direction)
    {
        var siblings = _searches.Where(item => item.ParentId == search.ParentId).OrderBy(item => item.SortOrder).ToList();
        var index = siblings.FindIndex(item => item.Id == search.Id);
        var other = index + direction;
        if (index < 0 || other < 0 || other >= siblings.Count) return;
        await MoveSearchToIndexAsync(search.Id, search.ParentId, other);
    }

    private async Task MoveSearchAsync(Guid id, Guid? parentId, int? sortOrder)
    {
        await MoveSearchToIndexAsync(id, parentId, sortOrder ?? _searches.Count);
    }

    private async Task MoveSearchToIndexAsync(Guid sourceId, Guid? parentId, int insertionIndex)
    {
        var source = _searches.FirstOrDefault(item => item.Id == sourceId);
        if (source is null || source.IsSystem || parentId == sourceId ||
            (parentId is { } parent && _searches.Any(item => item.Id == parent && IsDescendant(item, sourceId)))) return;
        var oldSiblings = _searches.Where(item => item.ParentId == source.ParentId && item.Id != sourceId)
            .OrderBy(item => item.SortOrder).ToList();
        var newSiblings = parentId == source.ParentId ? oldSiblings : _searches
            .Where(item => item.ParentId == parentId && item.Id != sourceId).OrderBy(item => item.SortOrder).ToList();
        newSiblings.Insert(Math.Clamp(insertionIndex, 0, newSiblings.Count), source with { ParentId = parentId });
        try
        {
            foreach (var siblings in parentId == source.ParentId ? new[] { newSiblings } : new[] { oldSiblings, newSiblings })
            {
                for (var position = 0; position < siblings.Count; position++)
                {
                    var updated = siblings[position] with { SortOrder = position };
                    await _workspaceRepository.SaveSearchAsync(updated, CancellationToken.None);
                    _searches[_searches.FindIndex(item => item.Id == updated.Id)] = updated;
                }
            }
            RebuildSearchNavItems();
            StatusText.Text = $"Перемещён поиск «{source.Name}»";
        }
        catch (Exception exception)
        {
            await LoadWorkspaceNavAsync();
            await ShowMessageAsync("Не удалось переместить поиск", exception.Message);
        }
    }

    private async Task MoveSearchRelativeAsync(Guid sourceId, SavedSearchRecord target, bool before)
    {
        var source = _searches.FirstOrDefault(item => item.Id == sourceId);
        if (source is null || source.IsSystem || source.Id == target.Id || IsDescendant(target, source.Id)) return;
        var siblings = _searches.Where(item => item.ParentId == target.ParentId && item.Id != sourceId)
            .OrderBy(item => item.SortOrder).ToList();
        var targetIndex = siblings.FindIndex(item => item.Id == target.Id);
        if (targetIndex < 0) return;
        await MoveSearchToIndexAsync(sourceId, target.ParentId, targetIndex + (before ? 0 : 1));
    }

    private async Task ChooseSearchParentAsync(SavedSearchRecord search)
    {
        var choices = _searches.Where(item => item.Id != search.Id && !IsDescendant(item, search.Id)).ToList();
        var box = new ComboBox { Header = "Родитель", ItemsSource = Enumerable.Repeat("Верхний уровень", 1)
            .Concat(choices.Select(item => item.Name)).ToArray(), SelectedIndex = 0 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Переместить поиск", Content = box,
            PrimaryButtonText = "Переместить", CloseButtonText = "Отмена" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && box.SelectedIndex >= 0)
            await MoveSearchAsync(search.Id, box.SelectedIndex == 0 ? null : choices[box.SelectedIndex - 1].Id, null);
    }

    private async Task DeleteSearchAsync(SavedSearchRecord search)
    {
        var descendants = _searches.Where(candidate => IsDescendant(candidate, search.Id)).ToArray();
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Удалить сохранённый поиск?",
            Content = $"«{search.Name}» и дочерние поиски ({descendants.Length}) исчезнут из панели. Тэги ресурсов сохранятся.",
            PrimaryButtonText = "Удалить", CloseButtonText = "Отмена" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await _workspaceRepository.DeleteSearchTreeAsync(search.Id, CancellationToken.None);
        await LoadWorkspaceNavAsync();
        _activeSearchId = null;
        PageTitle.Text = "Все ресурсы";
        ApplyFilter();
    }

    private bool IsDescendant(SavedSearchRecord candidate, Guid ancestorId)
    {
        var parent = candidate.ParentId;
        var visited = new HashSet<Guid>();
        while (parent is { } id && visited.Add(id))
        {
            if (id == ancestorId) return true;
            parent = _searches.FirstOrDefault(item => item.Id == id)?.ParentId;
        }
        return false;
    }
}
