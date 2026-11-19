using System.Text.Json;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoVncPanel
{
    private sealed record Destination(string Host, int Port, string? Title = null, string? Description = null,
        bool Pinned = false, bool Favorite = false)
    {
        public string Label => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
        public string Key => Label.ToLowerInvariant();
        public string Caption => string.IsNullOrEmpty(Title) ? Label : Title;

        public bool Matches(Destination other) => Key == other.Key;
    }

    private static string HistoryPath => Utils.GetConfigPath("trisko-vnc-history.json");
    private List<Destination> _history = [];

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
            {
                var saved = JsonSerializer.Deserialize<List<Destination>>(File.ReadAllText(HistoryPath)) ?? [];
                foreach (var item in saved) TriskoVncSettings.ValidateEndpoint(item.Host, item.Port);
                _history = saved;
            }
            RenderHistory();
        }
        catch (Exception ex) { ReportError(TriskoLocalization.Text("Не удалось прочитать историю VNC"), ex); }
    }

    private Control CreateConnectionRow(Destination target)
    {
        const string pinPath = "M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z";
        const string editPath = "M7 7H6a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-1 M20.385 6.585a2.1 2.1 0 0 0-2.97-2.97L9 12v3h3z M16 5l3 3";
        const string starPath = "M12 2l3.1 6.3 6.9 1-5 4.9 1.2 6.8-6.2-3.2L5.8 21 7 14.2 2 9.3l6.9-1z";
        var title = new TextBlock { Text = target.Caption, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var details = new StackPanel { Spacing = 3, Children = { title } };
        if (!string.IsNullOrEmpty(target.Title))
            details.Children.Add(new TextBlock { Text = target.Label, Classes = { "sectionSubtitle" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(target.Description))
            details.Children.Add(new TextBlock { Text = target.Description, Classes = { "sectionSubtitle" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        var connect = new Button { Content = TriskoLocalization.Text("Подключиться"), Classes = { "primaryAction" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        connect.Click += async (_, _) => await ConnectToAsync(target);
        var pinLabel = target.Pinned ? TriskoLocalization.Text("Открепить подключение") : TriskoLocalization.Text("Закрепить подключение");
        var pin = CreateCardAction("cardPinAction", pinLabel, pinPath, 14);
        pin.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        pin.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        pin.Margin = new Thickness(-14, -14, 0, 0);
        pin.Classes.Set("pinned", target.Pinned);
        pin.Click += (_, _) => UpdatePresentation(target, item => item with { Pinned = !item.Pinned });
        var favorite = target.Favorite ? TriskoLocalization.Text("Убрать из избранного") : TriskoLocalization.Text("Добавить в избранное");
        var star = CreateCardAction("favoriteAction", favorite, starPath, 16);
        star.Classes.Set("favorite", target.Favorite);
        star.Click += (_, _) => UpdatePresentation(target, item => item with { Favorite = !item.Favorite });
        var edit = CreateCardAction("cardEditAction", TriskoLocalization.Text("Изменить подключение"), editPath, 14);
        edit.Click += async (_, _) => await EditPresentationAsync(target);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Children = { star, edit } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { details, actions } };
        Grid.SetColumn(actions, 1);
        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12, Children = { header, connect, pin } };
        Grid.SetRow(connect, 1);
        var openItem = new MenuItem { Header = TriskoLocalization.Text("Подключиться") };
        openItem.Click += async (_, _) => await ConnectToAsync(target);
        var editItem = new MenuItem { Header = TriskoLocalization.Text("Изменить"), Icon = CreateCardIcon(editPath, 14) };
        editItem.Click += async (_, _) => await EditPresentationAsync(target);
        var pinItem = new MenuItem { Header = pinLabel, Icon = CreateCardIcon(pinPath, 14) };
        pinItem.Click += (_, _) => UpdatePresentation(target, item => item with { Pinned = !item.Pinned });
        var starItem = new MenuItem { Header = favorite, Icon = CreateCardIcon(starPath, 14) };
        starItem.Click += (_, _) => UpdatePresentation(target, item => item with { Favorite = !item.Favorite });
        var card = new Border { Classes = { "resourceCard" }, Padding = new Thickness(16), Child = content,
            ContextMenu = new ContextMenu { Items = { openItem, editItem, pinItem, starItem } } };
        card.Classes.Set("pinned", target.Pinned);
        return card;
    }

    private static PathIcon CreateCardIcon(string path, double size) =>
        new() { Width = size, Height = size, Data = Avalonia.Media.Geometry.Parse(path) };

    private static Button CreateCardAction(string style, string label, string path, double size)
    {
        var button = new Button { Content = CreateCardIcon(path, size), Classes = { style } };
        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    private void RenderHistory()
    {
        SetConnectionList(PinnedPanel, PinnedList, _history.Where(item => item.Pinned).OrderByDescending(item => item.Favorite));
        SetConnectionList(FavoritePanel, FavoriteList, _history.Where(item => item.Favorite && !item.Pinned));
        SetConnectionList(HistoryPanel, HistoryList, _history.Where(item => !item.Pinned && !item.Favorite));
    }

    private void SetConnectionList(StackPanel panel, ItemsControl list, IEnumerable<Destination> items)
    {
        var rows = items.Select(CreateConnectionRow).ToArray();
        list.ItemsSource = rows;
        panel.IsVisible = rows.Length > 0;
    }

    private void UpdatePresentation(Destination target, Func<Destination, Destination> edit)
    {
        try
        {
            var updated = _history.ToList();
            var index = updated.FindIndex(target.Matches);
            if (index < 0) return;
            updated[index] = edit(updated[index]);
            SaveHistory(updated);
        }
        catch (Exception ex) { ReportError(TriskoLocalization.Text("Не удалось сохранить оформление подключения"), ex); }
    }

    private async Task EditPresentationAsync(Destination target)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var title = new TextBox { Text = target.Title, PlaceholderText = TriskoLocalization.Text("Название"), MaxLength = 80 };
        var description = new TextBox { Text = target.Description, PlaceholderText = TriskoLocalization.Text("Описание"), MaxLength = 500,
            AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 76 };
        Avalonia.Automation.AutomationProperties.SetName(title, TriskoLocalization.Text("Название"));
        Avalonia.Automation.AutomationProperties.SetName(description, TriskoLocalization.Text("Описание"));
        var form = new StackPanel { Spacing = 8, Children = { title, description } };
        var dialog = new TriskoRemoteSettingsWindow(TriskoLocalization.Text("Оформление подключения"), target.Label, form, canApply: true);
        if (await dialog.ShowDialog<bool>(owner))
            UpdatePresentation(target, item => item with { Title = title.Text?.Trim(), Description = description.Text?.Trim() });
    }

    private void SaveHistory(List<Destination> updated)
    {
        var temporary = HistoryPath + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(updated));
            File.Move(temporary, HistoryPath, true);
        }
        finally { File.Delete(temporary); }
        _history = updated;
        RenderHistory();
    }

    private static async Task<(string Password, bool Save)?> AskPasswordAsync(Window owner, string label)
    {
        var field = new TextBox { PasswordChar = '●', PlaceholderText = TriskoLocalization.Text("Пароль VNC, до 8 символов ASCII") };
        var save = new CheckBox { Content = TriskoLocalization.Text("Сохранить пароль в Windows"), IsVisible = OperatingSystem.IsWindows() };
        var connect = new Button { Content = TriskoLocalization.Text("Подключиться"), IsDefault = true, IsEnabled = false };
        field.TextChanged += (_, _) => connect.IsEnabled = field.Text is { Length: > 0 and <= 8 } value && value.All(char.IsAscii);
        Avalonia.Automation.AutomationProperties.SetName(field, TriskoLocalization.Text("Пароль VNC"));
        var dialog = new Window { Title = TriskoLocalization.Text("Пароль VNC"), Width = 360, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(18), Spacing = 10,
                Children = { new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, field, save, connect } } };
        dialog.Opened += (_, _) => field.Focus();
        connect.Click += (_, _) => dialog.Close(true);
        var accepted = await dialog.ShowDialog<bool>(owner);
        var result = field.Text ?? "";
        field.Text = "";
        return accepted ? (result, save.IsChecked == true) : null;
    }
}
