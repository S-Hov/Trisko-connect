using Avalonia.Controls;
using Avalonia.Interactivity;
using ServiceLib.Geksa;

namespace v2rayN.Desktop.Geksa;

public partial class GeksaVpnSubscriptionWindow : Window
{
    private readonly GeksaVpnSubscriptionViewModel _viewModel;

    public GeksaVpnSubscriptionWindow()
        : this(null)
    {
    }

    public GeksaVpnSubscriptionWindow(GeksaVpnSubscriptionEditContext? context)
    {
        InitializeComponent();
        GeksaThemeVariants.CurrentVariantChanged += (_, _) =>
            RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        _viewModel = new GeksaVpnSubscriptionViewModel(context);
        DataContext = _viewModel;
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.TryBuild(out var request))
        {
            Close(request);
        }
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
