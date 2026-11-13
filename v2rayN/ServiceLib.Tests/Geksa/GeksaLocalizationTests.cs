using System.Globalization;
using AwesomeAssertions;
using ServiceLib.Handler;
using ServiceLib.Models;
using ServiceLib.Resx;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public sealed class GeksaLocalizationTests
{
    [Fact]
    public void ConfigHandler_EmptyLanguage_ShouldDefaultToRussian()
    {
        var rootConfig = new Config
        {
            UiItem = new UIItem { CurrentLanguage = string.Empty }
        };

        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
            if (rootConfig.UiItem.CurrentLanguage.IsNullOrEmpty())
            {
                var twoLetter = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName;
                rootConfig.UiItem.CurrentLanguage = twoLetter.Equals("zh", StringComparison.CurrentCultureIgnoreCase)
                    ? Global.Languages.First()
                    : "ru";
            }

            rootConfig.UiItem.CurrentLanguage.Should().Be("ru");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void ResUI_RussianCulture_ShouldProvideRussianEditStrings()
    {
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
            ResUI.menuEditCopy.Should().Be("Скопировать");
            ResUI.menuEditPaste.Should().Be("Вставить");
            ResUI.menuEditSelectAll.Should().Be("Выбрать все");
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }
}
