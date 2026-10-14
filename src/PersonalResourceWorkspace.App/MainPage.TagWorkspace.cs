using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace PersonalResourceWorkspace.App;

public sealed partial class MainPage
{
    private readonly Dictionary<Guid, NavigationViewItem> _searchNavItems = [];

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
        ShellNavigation.SelectedItem = _activeSearchId is { } active && _searchNavItems.TryGetValue(active, out var selected)
            ? selected : AllResourcesNavItem;
        UpdateSearchCounts();
    }

    private NavigationViewItem CreateSearchNavItem(SavedSearchRecord search)
    {
        var item = new NavigationViewItem
        {
            Tag = search,
            Content = CreateNavigationContent(search.Name, 0),
            Icon = new FontIcon { Glyph = search.Glyph, Foreground = BrushFromHex(search.Color) },
            AllowDrop = true,
            CanDrag = !search.IsSystem,
        };
        AutomationProperties.SetName(item, search.Name);
        item.DragStarting += (_, args) => args.Data.Properties["SearchId"] = search.Id.ToString();
        item.DragOver += (_, args) =>
        {
            if (args.DataView.Properties.ContainsKey("ResourceId"))
                args.AcceptedOperation = CanDropResourceOn(search) ? DataPackageOperation.Copy : DataPackageOperation.None;
            else if (args.DataView.Properties.ContainsKey("SearchId"))
                args.AcceptedOperation = DataPackageOperation.Move;
            args.Handled = true;
        };
        item.Drop += async (_, args) =>
        {
            args.Handled = true;
            if (args.DataView.Properties.TryGetValue("ResourceId", out var resourceValue) &&
                Guid.TryParse(resourceValue as string, out var resourceId))
            {
                var resource = _resources.FirstOrDefault(candidate => candidate.Id == resourceId);
                if (resource is not null) await AssignSearchTagsByDropAsync(resource, search);
            }
            else if (args.DataView.Properties.TryGetValue("SearchId", out var searchValue) &&
                     Guid.TryParse(searchValue as string, out var sourceId))
            {
                var position = args.GetPosition(item);
                if (position.Y < item.ActualHeight * 0.25)
                    await MoveSearchRelativeAsync(sourceId, search, before: true);
                else if (position.Y > item.ActualHeight * 0.75)
                    await MoveSearchRelativeAsync(sourceId, search, before: false);
                else
                    await MoveSearchAsync(sourceId, search.Id, null);
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
        (siblings[index], siblings[other]) = (siblings[other], siblings[index]);
        for (var position = 0; position < siblings.Count; position++)
        {
            var updated = siblings[position] with { SortOrder = position };
            await _workspaceRepository.SaveSearchAsync(updated, CancellationToken.None);
            var current = _searches.FindIndex(item => item.Id == updated.Id);
            _searches[current] = updated;
        }
        RebuildSearchNavItems();
    }

    private async Task MoveSearchAsync(Guid id, Guid? parentId, int? sortOrder)
    {
        var index = _searches.FindIndex(item => item.Id == id);
        if (index < 0 || _searches[index].IsSystem) return;
        var search = _searches[index];
        try
        {
            var updated = search with { ParentId = parentId, SortOrder = sortOrder ?? _searches.Count };
            await _workspaceRepository.SaveSearchAsync(updated, CancellationToken.None);
            _searches[index] = updated;
            RebuildSearchNavItems();
            StatusText.Text = $"Перемещён поиск «{search.Name}»";
        }
        catch (Exception exception)
        {
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
        siblings.Insert(targetIndex + (before ? 0 : 1), source with { ParentId = target.ParentId });
        try
        {
            for (var position = 0; position < siblings.Count; position++)
            {
                var updated = siblings[position] with { SortOrder = position };
                await _workspaceRepository.SaveSearchAsync(updated, CancellationToken.None);
                _searches[_searches.FindIndex(item => item.Id == updated.Id)] = updated;
            }
            RebuildSearchNavItems();
        }
        catch (Exception exception)
        {
            await LoadWorkspaceNavAsync();
            await ShowMessageAsync("Не удалось переместить поиск", exception.Message);
        }
    }

    private async Task ChooseSearchParentAsync(SavedSearchRecord search)
    {
        var choices = _searches.Where(item => item.Id != search.Id && !IsDescendant(item, search.Id)).ToList();
        var box = new ComboBox { Header = "Родитель", ItemsSource = choices.Select(item => item.Name).ToArray() };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Переместить поиск", Content = box,
            PrimaryButtonText = "Переместить", CloseButtonText = "Отмена" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && box.SelectedIndex >= 0)
            await MoveSearchAsync(search.Id, choices[box.SelectedIndex].Id, null);
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
