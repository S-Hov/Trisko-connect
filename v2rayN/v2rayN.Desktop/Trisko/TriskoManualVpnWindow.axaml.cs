namespace v2rayN.Desktop.Trisko;

public partial class TriskoManualVpnWindow : Window
{
    private readonly TriskoManualVpnViewModel _viewModel = new();

    public TriskoManualVpnWindow()
    {
        InitializeComponent();

        TriskoThemeVariants.CurrentVariantChanged += (s, e) => this.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        this.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;

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
