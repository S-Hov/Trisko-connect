namespace ServiceLib.Geksa;

public sealed record TriskoLanguageOption(string Code, string Name);

public partial class GeksaShellViewModel
{
    public IReadOnlyList<TriskoLanguageOption> LanguageOptions { get; } =
        [new("ru", TriskoLocalization.Text("Русский")), new("en", "English")];

    private TriskoLanguageOption? _selectedLanguage;
    public TriskoLanguageOption? SelectedLanguage
    {
        get => _selectedLanguage ?? LanguageOptions.FirstOrDefault(x => x.Code == _appConfig.UiItem.CurrentLanguage)
            ?? LanguageOptions[0];
        set
        {
            if (value is null || value == SelectedLanguage) return;
            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            _ = SaveLanguageAsync(value.Code);
        }
    }

    [Reactive] public partial string? LanguageStatus { get; set; }

    private async Task SaveLanguageAsync(string language)
    {
        var previous = _appConfig.UiItem.CurrentLanguage;
        _appConfig.UiItem.CurrentLanguage = language;
        if (await ConfigHandler.SaveConfig(_appConfig) != 0)
        {
            _appConfig.UiItem.CurrentLanguage = previous;
            _selectedLanguage = null;
            this.RaisePropertyChanged(nameof(SelectedLanguage));
            LanguageStatus = TriskoLocalization.Text("Не удалось сохранить язык интерфейса.");
            return;
        }
        LanguageStatus = language == "en"
            ? "Restart Trisko connect to apply English."
            : TriskoLocalization.Text("Перезапустите Trisko connect, чтобы применить русский язык.");
    }
}
