using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

[CollectionDefinition("Product localization", DisableParallelization = true)]
public sealed class ProductLocalizationCollection;

[Collection("Product localization")]
public class TriskoLocalizationTests
{
    [Fact]
    public void EnglishCatalogIsEmbeddedAndPreservesAllFormatArguments()
    {
        using var stream = typeof(TriskoLocalization).Assembly
            .GetManifestResourceStream("ServiceLib.Resx.Trisko.en.json");
        Assert.NotNull(stream);
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        Assert.True(catalog.Count > 1000);
        foreach (var (russian, english) in catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(english), russian);
            Assert.DoesNotMatch("[А-Яа-яЁё]", english);
            if (!Regex.IsMatch(russian, @"\{\d")) continue;
            Assert.Equal(CompositeFormat.Parse(russian).MinimumArgumentCount,
                CompositeFormat.Parse(english).MinimumArgumentCount);
            Assert.Equal(Regex.Matches(russian, @"\{\d+[^{}]*\}").Select(x => x.Value).Order(),
                Regex.Matches(english, @"\{\d+[^{}]*\}").Select(x => x.Value).Order());
        }
    }

    [Fact]
    public void LanguageSelectionTranslatesLabelsValidationAndFormattedMessages()
    {
        try
        {
            TriskoLocalization.Initialize("en");
            Assert.Equal("Settings", TriskoLocalization.Text("Настройки"));
            Assert.DoesNotMatch("[А-Яа-яЁё]", TriskoLocalization.Format("Настроено VPN-подписок: {0}", 3));
            var invalid = new GeksaVpnSubscriptionViewModel("http://example.com/sub");
            Assert.False(invalid.TryBuild(out _));
            Assert.DoesNotMatch("[А-Яа-яЁё]", invalid.ValidationMessage!);
            foreach (var label in typeof(TriskoStrings).GetProperties())
                Assert.DoesNotMatch("[А-Яа-яЁё]", (string)label.GetValue(null)!);
        }
        finally { TriskoLocalization.Initialize("ru"); }
        Assert.Equal("Настройки", TriskoLocalization.Text("Настройки"));
    }
}
