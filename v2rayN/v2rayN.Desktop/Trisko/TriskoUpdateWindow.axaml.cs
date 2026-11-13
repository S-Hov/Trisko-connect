namespace v2rayN.Desktop.Trisko;

public partial class TriskoUpdateWindow : Window
{
    private readonly bool _checkImmediately;

    public TriskoUpdateWindow()
    {
        InitializeComponent();

        TriskoThemeVariants.CurrentVariantChanged += (s, e) => this.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        this.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;

    }

    public TriskoUpdateWindow(TriskoUpdateViewModel viewModel, bool checkImmediately)
        : this()
    {
        DataContext = viewModel;
        _checkImmediately = checkImmediately;
        Loaded += OnUpdateWindowLoaded;
    }

    private async void OnUpdateWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnUpdateWindowLoaded;
        if (DataContext is TriskoUpdateViewModel viewModel)
        {
            await viewModel.InitializeAsync(_checkImmediately);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is TriskoUpdateViewModel { IsBusy: true })
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
