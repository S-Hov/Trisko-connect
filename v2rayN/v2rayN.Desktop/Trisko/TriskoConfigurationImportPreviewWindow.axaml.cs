using Avalonia.Controls;
using Avalonia.Interactivity;
using ServiceLib.Trisko;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoConfigurationImportPreviewWindow : Window
{
    public TriskoConfigurationImportPreviewWindow()
    {
        InitializeComponent();
        TriskoThemeVariants.CurrentVariantChanged += (_, _) =>
            RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
    }

    public TriskoConfigurationImportPreviewWindow(TriskoConfigurationImportPreview preview)
        : this()
    {
        DataContext = preview ?? throw new ArgumentNullException(nameof(preview));
    }

    private void InstallClick(object? sender, RoutedEventArgs e)
    {
        Close(new TriskoConfigurationImportDecision(
            Confirmed: true,
            KeepLocalChanges: KeepLocalChangesCheckBox.IsChecked != false));
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new TriskoConfigurationImportDecision(
            Confirmed: false,
            KeepLocalChanges: true));
    }
}
