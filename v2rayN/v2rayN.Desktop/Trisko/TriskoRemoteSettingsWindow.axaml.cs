namespace v2rayN.Desktop.Trisko;

public partial class TriskoRemoteSettingsWindow : Window
{
    public TriskoRemoteSettingsWindow()
    {
        InitializeComponent();
        RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
    }

    public TriskoRemoteSettingsWindow(string title, string description, Control form, bool canApply = false)
        : this()
    {
        Title = Heading.Text = title;
        Description.Text = description;
        Form.Content = form;
        Apply.IsVisible = canApply;
        if (canApply) Dismiss.Content = TriskoLocalization.Text("Отмена");
    }

    public static async Task<string?> ShowFormAsync(Control source, ContentControl slot, string title, string description)
    {
        if (TopLevel.GetTopLevel(source) is not Window owner || slot.Content is not Control form) return null;
        slot.Content = null;
        var window = new TriskoRemoteSettingsWindow(title, description, form) { DataContext = source.DataContext };
        try
        {
            return await window.ShowDialog<string?>(owner);
        }
        finally
        {
            window.Form.Content = null;
            slot.Content = form;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void Dismiss_Click(object? sender, RoutedEventArgs e) => Close();
    private void Apply_Click(object? sender, RoutedEventArgs e) => Close(true);
}
