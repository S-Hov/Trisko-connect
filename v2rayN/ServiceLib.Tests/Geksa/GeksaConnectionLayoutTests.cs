using System.Text.Json;
using AwesomeAssertions;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaConnectionLayoutTests
{
    [Fact]
    public void ExistingSettingsWithoutLayoutFlags_HideServiceBypassBlock()
    {
        var ui = JsonSerializer.Deserialize<UIItem>("{\"GeksaShowConnectionNav\":true}")!;

        Visibility(ui).Should().Equal(true, true, true, true);
    }

    [Fact]
    public void EveryBlockCombination_SurvivesSettingsRoundTrip()
    {
        for (int mask = 0; mask < 32; mask++)
        {
            var ui = new UIItem
            {
                GeksaShowConnectionStatus = (mask & 1) != 0,
                GeksaShowConnectionResources = (mask & 2) != 0,
                GeksaShowConnectionConfiguration = (mask & 8) != 0,
                GeksaShowConnectionLogs = (mask & 16) != 0
            };

            var restored = JsonSerializer.Deserialize<UIItem>(JsonSerializer.Serialize(ui))!;

            Visibility(restored).Should().Equal(Visibility(ui));
        }
    }

    private static bool[] Visibility(UIItem ui) =>
    [
        ui.GeksaShowConnectionStatus,
        ui.GeksaShowConnectionResources,
        ui.GeksaShowConnectionConfiguration,
        ui.GeksaShowConnectionLogs
    ];
}
