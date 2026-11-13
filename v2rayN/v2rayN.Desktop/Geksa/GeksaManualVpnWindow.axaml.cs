namespace v2rayN.Desktop.Geksa;

public partial class GeksaManualVpnWindow : Window
{
    private readonly GeksaManualVpnViewModel _viewModel = new();

    public GeksaManualVpnWindow()
    {
        InitializeComponent();

        GeksaThemeVariants.CurrentVariantChanged += (s, e) => this.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        this.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;

        DataContext = _viewModel;
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.TryBuild(out var profile))
        {
            Close(profile);
        }
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
