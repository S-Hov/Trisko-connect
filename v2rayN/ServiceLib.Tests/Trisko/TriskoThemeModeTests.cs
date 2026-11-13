using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoThemeModeTests
{
    [Theory]
    [InlineData(TriskoThemeMode.Brand, TriskoThemeMode.Dark)]
    [InlineData(TriskoThemeMode.Dark, TriskoThemeMode.Light)]
    [InlineData(TriskoThemeMode.Light, TriskoThemeMode.Brand)]
    public void ThemeCycle_ShouldTransitionCorrectly(TriskoThemeMode current, TriskoThemeMode expectedNext)
    {
        var next = current switch
        {
            TriskoThemeMode.Brand => TriskoThemeMode.Dark,
            TriskoThemeMode.Dark => TriskoThemeMode.Light,
            _ => TriskoThemeMode.Brand
        };

        next.Should().Be(expectedNext);
    }

    [Theory]
    [InlineData(TriskoThemeMode.Brand, "Синяя")]
    [InlineData(TriskoThemeMode.Dark, "Тёмная")]
    [InlineData(TriskoThemeMode.Light, "Светлая")]
    public void ThemeSummary_ShouldContainThemeName(TriskoThemeMode mode, string expectedName)
    {
        var summary = mode switch
        {
            TriskoThemeMode.Brand => "Активна: Синяя (фирменный корпоративный стиль)",
            TriskoThemeMode.Dark => "Активна: Тёмная (для комфортной работы ночью)",
            TriskoThemeMode.Light => "Активна: Светлая (высококонтрастный дневной режим)",
            _ => "Выбор темы оформления"
        };

        summary.Should().Contain(expectedName);
    }
}
