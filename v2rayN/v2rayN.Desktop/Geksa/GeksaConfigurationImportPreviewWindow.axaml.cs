using Avalonia.Controls;
using Avalonia.Interactivity;
using ServiceLib.Geksa;

namespace v2rayN.Desktop.Geksa;

public partial class GeksaConfigurationImportPreviewWindow : Window
{
    public GeksaConfigurationImportPreviewWindow()
    {
        InitializeComponent();
        GeksaThemeVariants.CurrentVariantChanged += (_, _) =>
            RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
    }

    public GeksaConfigurationImportPreviewWindow(GeksaConfigurationImportPreview preview)
        : this()
    {
        DataContext = preview ?? throw new ArgumentNullException(nameof(preview));
    }

    private void InstallClick(object? sender, RoutedEventArgs e)
    {
        Close(new GeksaConfigurationImportDecision(
            Confirmed: true,
            KeepLocalChanges: KeepLocalChangesCheckBox.IsChecked != false));
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new GeksaConfigurationImportDecision(
            Confirmed: false,
            KeepLocalChanges: true));
    }
}
