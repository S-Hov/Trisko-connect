using System.Globalization;

namespace ServiceLib.Geksa;

/// <summary>Product UI translations; resource selection is fixed for an application session.</summary>
public static class TriskoLocalization
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() =>
    {
        using var stream = typeof(TriskoLocalization).Assembly
            .GetManifestResourceStream("ServiceLib.Resx.Trisko.en.json")
            ?? throw new InvalidOperationException("Trisko English catalog is missing.");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    public static string Language { get; private set; } = "ru";

    public static void Initialize(string? language)
    {
        Language = language == "en" ? "en" : "ru";
        var culture = CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        ResUI.Culture = culture;
    }

    public static string Text(string russian) => Language == "en" && English.Value.TryGetValue(russian, out var english)
        ? english : russian;

    public static string Format(string russian, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Text(russian), arguments);
}
