namespace v2rayN.Desktop.Views;

public partial class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        InitializeComponent();
    }

    public void SetLanguage()
    {
        LoadingText.Text = TriskoLocalization.Text("Загрузка…");
    }
}
