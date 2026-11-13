using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoFormatHelpWindow : Window
{
    public const string Gac3Example = @"{
  ""schemaVersion"": 1,
  ""configurationVersion"": ""1.0"",
  ""revision"": 1,
  ""resources"": [
    {
      ""id"": ""rdp-finance"",
      ""type"": ""rdp"",
      ""displayName"": ""Сервер 1С / Бухгалтерия"",
      ""description"": ""Основной сервер бухгалтерии"",
      ""host"": ""10.10.1.50"",
      ""port"": 3389,
      ""userName"": ""CORP\\user"",
      ""requiresVpn"": true,
      ""fullScreen"": true,
      ""multiMonitor"": false,
      ""isPinned"": true
    },
    {
      ""id"": ""smb-documents"",
      ""type"": ""smb"",
      ""displayName"": ""Общие документы"",
      ""description"": ""Сетевой диск отдела"",
      ""host"": ""fileserver.corp.local"",
      ""port"": 445,
      ""path"": ""share/documents"",
      ""driveLetter"": ""Z:"",
      ""requiresVpn"": true,
      ""isPinned"": false
    },
    {
      ""id"": ""ssh-gateway"",
      ""type"": ""ssh"",
      ""displayName"": ""Linux Gateway"",
      ""description"": ""Шлюз серверов разработки"",
      ""host"": ""10.10.2.1"",
      ""port"": 22,
      ""userName"": ""admin"",
      ""requiresVpn"": true,
      ""isPinned"": false
    }
  ]
}";

    public const string RdpExample = @"{
  ""id"": ""rdp-workstation"",
  ""type"": ""rdp"",
  ""displayName"": ""Рабочий ПК"",
  ""description"": ""Удалённый рабочий стол"",
  ""host"": ""192.168.1.100"",
  ""port"": 3389,
  ""userName"": ""CORP\\ivanov"",
  ""requiresVpn"": true,
  ""fullScreen"": true,
  ""multiMonitor"": false,
  ""isPinned"": false
}";

    public const string SshExample = @"{
  ""id"": ""ssh-server"",
  ""type"": ""ssh"",
  ""displayName"": ""Сервер баз данных"",
  ""description"": ""SSH доступ к PostgreSQL"",
  ""host"": ""db.corp.local"",
  ""port"": 22,
  ""userName"": ""postgres"",
  ""requiresVpn"": true,
  ""isPinned"": false
}";

    public const string SmbExample = @"{
  ""id"": ""smb-shared"",
  ""type"": ""smb"",
  ""displayName"": ""Сетевая папка проектов"",
  ""description"": ""Документы и проекты отдела"",
  ""host"": ""nas.corp.local"",
  ""port"": 445,
  ""path"": ""projects/current"",
  ""driveLetter"": ""Z:"",
  ""requiresVpn"": true,
  ""isPinned"": false
}";

    public TriskoFormatHelpWindow()
    {
        InitializeComponent();

        TriskoThemeVariants.CurrentVariantChanged += (s, e) => this.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;
        this.RequestedThemeVariant = TriskoThemeVariants.CurrentVariant;

        Gac3TextBox.Text = Gac3Example;
        RdpTextBox.Text = RdpExample;
        SshTextBox.Text = SshExample;
        SmbTextBox.Text = SmbExample;
    }

    private async void CopyGac3ExampleClick(object? sender, RoutedEventArgs e)
    {
        await CopyToClipboardAsync(Gac3Example, TriskoLocalization.Text("Пример .gac3 скопирован в буфер обмена!"));
    }

    private async void CopyRdpExampleClick(object? sender, RoutedEventArgs e)
    {
        await CopyToClipboardAsync(RdpExample, TriskoLocalization.Text("Пример RDP JSON скопирован!"));
    }

    private async void CopySshExampleClick(object? sender, RoutedEventArgs e)
    {
        await CopyToClipboardAsync(SshExample, TriskoLocalization.Text("Пример SSH JSON скопирован!"));
    }

    private async void CopySmbExampleClick(object? sender, RoutedEventArgs e)
    {
        await CopyToClipboardAsync(SmbExample, TriskoLocalization.Text("Пример SMB JSON скопирован!"));
    }

    private async Task CopyToClipboardAsync(string text, string successMessage)
    {
        await AvaUtils.SetClipboardData(this, text);
        StatusMessageText.Text = successMessage;
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
