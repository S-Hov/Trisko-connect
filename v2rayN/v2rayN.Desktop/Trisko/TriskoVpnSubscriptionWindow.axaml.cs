using Avalonia.Controls;
using Avalonia.Interactivity;
using ServiceLib.Trisko;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoVpnSubscriptionWindow : Window
{
    private readonly TriskoVpnSubscriptionViewModel _viewModel;

    public TriskoVpnSubscriptionWindow()
        : this(null)
    {
    }

    public TriskoVpnSubscriptionWindow(TriskoVpnSubscriptionEditContext? context)
    {
        InitializeComponent();
        TriskoThemeVariants.CurrentVariantChanged += (_, _) =>
            RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        _viewModel = new TriskoVpnSubscriptionViewModel(context);
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
