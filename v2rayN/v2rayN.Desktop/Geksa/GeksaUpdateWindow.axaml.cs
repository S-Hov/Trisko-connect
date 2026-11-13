namespace v2rayN.Desktop.Geksa;

public partial class GeksaUpdateWindow : Window
{
    private readonly bool _checkImmediately;

    public GeksaUpdateWindow()
    {
        InitializeComponent();

        GeksaThemeVariants.CurrentVariantChanged += (s, e) => this.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        this.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;

    }

    public GeksaUpdateWindow(GeksaUpdateViewModel viewModel, bool checkImmediately)
        : this()
    {
        DataContext = viewModel;
        _checkImmediately = checkImmediately;
        Loaded += OnUpdateWindowLoaded;
    }

    private async void OnUpdateWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnUpdateWindowLoaded;
        if (DataContext is GeksaUpdateViewModel viewModel)
        {
            await viewModel.InitializeAsync(_checkImmediately);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is GeksaUpdateViewModel { IsBusy: true })
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
