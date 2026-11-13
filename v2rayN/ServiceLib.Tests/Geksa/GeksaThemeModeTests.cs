using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaThemeModeTests
{
    [Theory]
    [InlineData(GeksaThemeMode.Brand, GeksaThemeMode.Dark)]
    [InlineData(GeksaThemeMode.Dark, GeksaThemeMode.Light)]
    [InlineData(GeksaThemeMode.Light, GeksaThemeMode.Brand)]
    public void ThemeCycle_ShouldTransitionCorrectly(GeksaThemeMode current, GeksaThemeMode expectedNext)
    {
        var next = current switch
        {
            GeksaThemeMode.Brand => GeksaThemeMode.Dark,
            GeksaThemeMode.Dark => GeksaThemeMode.Light,
            _ => GeksaThemeMode.Brand
        };

        next.Should().Be(expectedNext);
    }

    [Theory]
    [InlineData(GeksaThemeMode.Brand, "Синяя")]
    [InlineData(GeksaThemeMode.Dark, "Тёмная")]
    [InlineData(GeksaThemeMode.Light, "Светлая")]
    public void ThemeSummary_ShouldContainThemeName(GeksaThemeMode mode, string expectedName)
    {
        var summary = mode switch
        {
            GeksaThemeMode.Brand => "Активна: Синяя (фирменный корпоративный стиль)",
            GeksaThemeMode.Dark => "Активна: Тёмная (для комфортной работы ночью)",
            GeksaThemeMode.Light => "Активна: Светлая (высококонтрастный дневной режим)",
            _ => "Выбор темы оформления"
        };

        summary.Should().Contain(expectedName);
    }
}
