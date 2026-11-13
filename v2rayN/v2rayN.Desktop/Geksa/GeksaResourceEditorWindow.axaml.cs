using System.IO;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ServiceLib.Geksa;

namespace v2rayN.Desktop.Geksa;

public partial class GeksaResourceEditorWindow : Window
{
    private readonly GeksaResourceEditorViewModel _viewModel;

    public GeksaResourceEditorWindow()
        : this(new GeksaResourceEditRequest(GeksaResourceType.Rdp))
    {
    }

    public GeksaResourceEditorWindow(GeksaResourceEditRequest request)
    {
        InitializeComponent();

        GeksaThemeVariants.CurrentVariantChanged += (s, e) => this.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        this.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;

        _viewModel = new GeksaResourceEditorViewModel(request);
        DataContext = _viewModel;
    }

    private async void ImportFromFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = TriskoLocalization.Format("Выберите файл одного {0}-подключения (.json)", _viewModel.TypeBadge),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(TriskoLocalization.Text("Одно подключение Trisko (*.json)"))
                {
                    Patterns = ["*.json"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            var bytes = await GeksaConfigurationFileIO.ReadLimitedAsync(stream);
            try
            {
                var resource = GeksaCorporateConfigurationCodec.ParseResourceProfile(bytes);
                _viewModel.ApplyProfile(resource);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (GeksaConfigurationException ex)
        {
            _viewModel.OperationMessage = null;
            _viewModel.ValidationMessage = ex.UserMessage;
        }
        catch (Exception ex)
        {
            _viewModel.OperationMessage = null;
            _viewModel.ValidationMessage = TriskoLocalization.Format("Ошибка чтения файла: {0}", ex.Message);
        }
    }

    private async void ExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuild(out var resource) || resource is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var defaultName = GeksaConfigurationFileIO.SanitizeSuggestedFileName(
            $"{resource.Type}-{resource.DisplayName.Trim()}",
            $"{resource.Type}-connection");

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = TriskoLocalization.Text("Экспорт подключения"),
            SuggestedFileName = defaultName,
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType(TriskoLocalization.Text("Файл подключения JSON (*.json)"))
                {
                    Patterns = ["*.json"]
                },
            ]
        });

        if (file is null)
        {
            return;
        }

        try
        {
            var content = GeksaCorporateConfigurationCodec.SerializeResourceProfile(resource);
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await GeksaConfigurationFileIO.WriteTextAtomicallyAsync(localPath, content);
            }
            else
            {
                await using var stream = await file.OpenWriteAsync();
                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }
                await using var writer = new StreamWriter(
                    stream,
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await writer.WriteAsync(content);
                await writer.FlushAsync();
            }
            _viewModel.ValidationMessage = null;
            _viewModel.OperationMessage =
                TriskoLocalization.Format("Подключение экспортировано в «{0}». Файл содержит только одну карточку, без VPN и паролей.", file.Name);
        }
        catch (GeksaConfigurationException ex)
        {
            _viewModel.OperationMessage = null;
            _viewModel.ValidationMessage = ex.UserMessage;
        }
        catch (Exception ex)
        {
            _viewModel.OperationMessage = null;
            _viewModel.ValidationMessage = TriskoLocalization.Format("Ошибка экспорта: {0}", ex.Message);
        }
    }

    private async void HelpFormatClick(object? sender, RoutedEventArgs e)
    {
        var helpWindow = new GeksaFormatHelpWindow();
        await helpWindow.ShowDialog(this);
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.TryBuild(out var resource))
        {
            Close(new GeksaResourceEditResult(resource));
        }
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        Close(new GeksaResourceEditResult(DeleteRequested: true));
    }

    private void DisconnectSmbClick(object? sender, RoutedEventArgs e)
    {
        Close(new GeksaResourceEditResult(DisconnectSmbRequested: true));
    }

    private void CancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
