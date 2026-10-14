using PersonalResourceWorkspace.Application.Models;
using PersonalResourceWorkspace.Application.Resources;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PersonalResourceWorkspace.App;

public sealed partial class MainPage
{
    private static readonly string[] NewTagChoice = ["Создать новый тэг"];
    private void UpdateTagFilterChips()
    {
        if (BaseTagsPanel is null || TemporaryTagsPanel is null) return;
        BaseTagsPanel.Children.Clear();
        TemporaryTagsPanel.Children.Clear();
        foreach (var id in ActiveRequiredTags())
            BaseTagsPanel.Children.Add(new TextBlock { Text = TagName(id), VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 3, 6, 3), Opacity = 0.75 });
        foreach (var id in _temporaryTags)
        {
            var button = new Button { Content = $"{TagName(id)} ×", Padding = new Thickness(6, 2, 6, 2) };
            button.Click += (_, _) => { _temporaryTags.Remove(id); UpdateTagFilterChips(); ApplyFilter(); };
            TemporaryTagsPanel.Children.Add(button);
        }
        UpdateCreateAvailability();
    }

    private void UpdateCreateAvailability()
    {
        if (AddResourceButton is not null)
            AddResourceButton.IsEnabled = !ActiveRequiredTags().Concat(_temporaryTags).Contains(SystemTags.TrashId);
    }

    private void TagFilter_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var text = sender.Text.Trim();
        sender.ItemsSource = text.Length == 0 ? [] : _tags
            .Where(tag => tag.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) &&
                          !_temporaryTags.Contains(tag.Id))
            .Select(tag => tag.Name).ToArray();
    }

    private async void TagFilter_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var name = (args.ChosenSuggestion as string ?? args.QueryText).Trim();
        if (name.Length == 0) return;
        try
        {
            var tag = await GetOrCreateTagAsync(name);
            _temporaryTags.Add(tag.Id);
            sender.Text = string.Empty;
            UpdateTagFilterChips();
            ApplyFilter();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async Task<TagRecord> GetOrCreateTagAsync(string name)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0) throw new InvalidOperationException("Введите имя тэга.");
        var existing = _tags.FirstOrDefault(tag => string.Equals(tag.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var created = new TagRecord(Guid.NewGuid(), normalized, false);
        await _workspaceRepository.SaveTagAsync(created, CancellationToken.None);
        _tags.Add(created);
        return created;
    }

    private async void SaveSearch_Click(object sender, RoutedEventArgs e)
    {
        var conditionIds = ActiveRequiredTags().Concat(_temporaryTags).Distinct().ToArray();
        var search = new SavedSearchRecord(Guid.NewGuid(), "", null, "\uE721", "#7EA7FF", _searches.Count, false, conditionIds);
        await ShowSearchEditorAsync(search, isNew: true);
    }

    private Task EditSearchAsync(SavedSearchRecord search) => ShowSearchEditorAsync(search, isNew: false);

    private async Task ShowSearchEditorAsync(SavedSearchRecord search, bool isNew)
    {
        var nameBox = new TextBox { Header = "Имя поиска", Text = search.Name, IsEnabled = !search.IsSystem };
        var iconChoices = new (string Name, string Glyph)[]
        {
            ("Поиск", "\uE721"), ("Главная", "\uE80F"), ("Папка", "\uE8B7"),
            ("Ссылка", "\uE71B"), ("Избранное", "\uE734"), ("Закрепить", "\uE718"),
            ("Документ", "\uE8A5"), ("Фильтр", "\uE71C"),
        };
        if (!iconChoices.Any(choice => choice.Glyph == search.Glyph))
            iconChoices = [("Текущая иконка", search.Glyph), .. iconChoices];
        var iconBox = new ComboBox { Header = "Иконка", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var choice in iconChoices)
        {
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            preview.Children.Add(new FontIcon { Glyph = choice.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets") });
            preview.Children.Add(new TextBlock { Text = choice.Name, VerticalAlignment = VerticalAlignment.Center });
            iconBox.Items.Add(new ComboBoxItem { Content = preview, Tag = choice.Glyph });
        }
        iconBox.SelectedIndex = Math.Max(0, Array.FindIndex(iconChoices, choice => choice.Glyph == search.Glyph));
        var initialColor = BrushFromHex(search.Color).Color;
        var colorPicker = new ColorPicker
        {
            Color = initialColor,
            IsAlphaEnabled = false,
        };
        var parents = _searches.Where(item => item.Id != search.Id && !IsDescendant(item, search.Id)).ToArray();
        var parentBox = new ComboBox { Header = "Родитель (для отображения)", HorizontalAlignment = HorizontalAlignment.Stretch };
        parentBox.Items.Add("Верхний уровень");
        foreach (var parent in parents) parentBox.Items.Add(parent.Name);
        parentBox.SelectedIndex = search.ParentId is { } currentParent
            ? Math.Max(0, Array.FindIndex(parents, item => item.Id == currentParent) + 1) : 0;
        parentBox.IsEnabled = !search.IsSystem;
        var conditions = new StackPanel { Spacing = 4 };
        var selectors = new List<(Guid Id, CheckBox Box)>();
        foreach (var tag in _tags)
        {
            var check = new CheckBox { Content = tag.Name, IsChecked = search.RequiredTagIds.Contains(tag.Id),
                IsEnabled = !search.IsSystem };
            conditions.Children.Add(check);
            selectors.Add((tag.Id, check));
        }
        var fields = new StackPanel { Spacing = 10, MinWidth = 380 };
        fields.Children.Add(nameBox);
        fields.Children.Add(iconBox);
        fields.Children.Add(new TextBlock { Text = "Цвет" });
        fields.Children.Add(colorPicker);
        fields.Children.Add(parentBox);
        fields.Children.Add(new TextBlock { Text = isNew
            ? "Сохраняются только обязательные тэги. Текстовый запрос не сохраняется."
            : "Условия поиска изменяются только здесь.", TextWrapping = TextWrapping.Wrap });
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void UpdateSummary()
        {
            var names = selectors.Where(item => item.Box.IsChecked == true).Select(item => TagName(item.Id)).ToArray();
            summary.Text = $"Итоговые условия: {(names.Length == 0 ? "без тэгов" : string.Join(" И ", names))}";
        }
        foreach (var selector in selectors)
        {
            selector.Box.Checked += (_, _) => UpdateSummary();
            selector.Box.Unchecked += (_, _) => UpdateSummary();
        }
        UpdateSummary();
        fields.Children.Add(summary);
        fields.Children.Add(conditions);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = isNew ? "Сохранить поиск" : "Редактировать поиск",
            Content = new ScrollViewer { Content = fields, MaxHeight = 510 },
            PrimaryButtonText = isNew ? "Создать" : "Сохранить", CloseButtonText = "Отмена" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
                args.Cancel = true;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var updated = search with
        {
            Name = nameBox.Text.Trim(),
            Glyph = (iconBox.SelectedItem as ComboBoxItem)?.Tag as string ?? iconChoices[0].Glyph,
            Color = $"#{colorPicker.Color.R:X2}{colorPicker.Color.G:X2}{colorPicker.Color.B:X2}",
            ParentId = search.IsSystem || parentBox.SelectedIndex <= 0 ? null : parents[parentBox.SelectedIndex - 1].Id,
            RequiredTagIds = search.IsSystem ? search.RequiredTagIds : selectors.Where(item => item.Box.IsChecked == true).Select(item => item.Id).ToArray(),
        };
        try
        {
            await _workspaceRepository.SaveSearchAsync(updated, CancellationToken.None);
            if (isNew) _searches.Add(updated);
            else _searches[_searches.FindIndex(item => item.Id == updated.Id)] = updated;
            _activeSearchId = updated.Id;
            PageTitle.Text = updated.Name;
            if (isNew)
            {
                _temporaryTags.Clear();
                SearchBox.Text = string.Empty;
                TagFilterBox.Text = string.Empty;
            }
            RebuildSearchNavItems();
            UpdateTagFilterChips();
            ApplyFilter();
        }
        catch (Exception exception) { await ShowMessageAsync("Не удалось сохранить поиск", exception.Message); }
    }

    private async void ManageTags_Click(object sender, RoutedEventArgs e) => await ManageTagsAsync();

    private async Task ManageTagsAsync()
    {
        while (true)
        {
            var candidates = _tags.Where(tag => !tag.IsSystem).ToArray();
            var box = new ComboBox { Header = "Тэг", HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = NewTagChoice.Concat(candidates.Select(tag => tag.Name)).ToArray(), SelectedIndex = 0 };
            var newName = new TextBox { Header = "Имя нового тэга или новое имя выбранного" };
            var fields = new StackPanel { Spacing = 10, MinWidth = 340 };
            fields.Children.Add(new TextBlock { Text = "«Входящие» и «Корзина» нельзя переименовать или удалить." });
            fields.Children.Add(box);
            fields.Children.Add(newName);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Управление тэгами", Content = fields,
                PrimaryButtonText = "Создать / переименовать", SecondaryButtonText = "Удалить выбранный", CloseButtonText = "Готово" };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) return;
            try
            {
                if (result == ContentDialogResult.Primary)
                {
                    var name = newName.Text.Trim();
                    if (name.Length == 0) continue;
                    if (box.SelectedIndex <= 0)
                        await GetOrCreateTagAsync(name);
                    else
                    {
                        var old = candidates[box.SelectedIndex - 1];
                        await _workspaceRepository.SaveTagAsync(old with { Name = name }, CancellationToken.None);
                        _tags[_tags.FindIndex(tag => tag.Id == old.Id)] = old with { Name = name };
                    }
                }
                else if (box.SelectedIndex > 0)
                {
                    var tag = candidates[box.SelectedIndex - 1];
                    var searches = _searches.Where(search => search.RequiredTagIds.Contains(tag.Id)).ToArray();
                    if (searches.Length > 0)
                    {
                        await ShowMessageAsync("Тэг используется в поисках", string.Join(", ", searches.Select(search => search.Name)));
                        continue;
                    }
                    var count = _resources.Count(resource => resource.TagIds.Contains(tag.Id));
                    var confirm = new ContentDialog { XamlRoot = XamlRoot, Title = $"Удалить тэг «{tag.Name}»?",
                        Content = $"Тэг будет снят с {count} ресурсов.", PrimaryButtonText = "Удалить", CloseButtonText = "Отмена" };
                    if (await confirm.ShowAsync() != ContentDialogResult.Primary) continue;
                    await _workspaceRepository.DeleteTagAsync(tag.Id, CancellationToken.None);
                    _tags.RemoveAll(candidate => candidate.Id == tag.Id);
                    foreach (var resource in _resources)
                        if (resource.TagIds.Remove(tag.Id) && !IsInTrash(resource) &&
                            resource.TagIds.All(id => id == SystemTags.InboxId)) resource.TagIds.Add(SystemTags.InboxId);
                    _temporaryTags.Remove(tag.Id);
                }
                UpdateTagFilterChips();
                ApplyFilter();
            }
            catch (Exception exception) { await ShowMessageAsync("Не удалось изменить тэг", exception.Message); }
        }
    }

    private async Task EmptyTagTrashAsync()
    {
        var trashed = _resources.Where(IsInTrash).ToArray();
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Очистить корзину?",
            Content = $"Безвозвратно удалить ресурсов: {trashed.Length}?", PrimaryButtonText = "Очистить", CloseButtonText = "Отмена" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var resource in trashed)
        {
            await _workspaceRepository.DeleteResourceAsync(resource.Id, CancellationToken.None);
            _resources.Remove(resource);
        }
        ApplyFilter();
    }

    private async void RestoreResourceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: WorkspaceResource resource } || !IsInTrash(resource)) return;
        resource.TagIds = SystemTags.AfterRestore(resource.TagIds).ToHashSet();
        await SaveResourceAsync(resource);
        ApplyFilter();
        StatusText.Text = $"Восстановлено: {resource.Title}";
    }
}
