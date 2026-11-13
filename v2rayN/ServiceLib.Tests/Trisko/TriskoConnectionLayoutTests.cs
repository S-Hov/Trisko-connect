using System.Text.Json;
using AwesomeAssertions;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoConnectionLayoutTests
{
    [Fact]
    public void ExistingSettingsWithoutLayoutFlags_HideServiceBypassBlock()
    {
        var ui = JsonSerializer.Deserialize<UIItem>("{\"TriskoShowConnectionNav\":true}")!;

        Visibility(ui).Should().Equal(true, true, true, true);
    }

    [Fact]
    public void EveryBlockCombination_SurvivesSettingsRoundTrip()
    {
        for (int mask = 0; mask < 32; mask++)
        {
            var ui = new UIItem
            {
                TriskoShowConnectionStatus = (mask & 1) != 0,
                TriskoShowConnectionResources = (mask & 2) != 0,
                TriskoShowConnectionConfiguration = (mask & 8) != 0,
                TriskoShowConnectionLogs = (mask & 16) != 0
            };

            var restored = JsonSerializer.Deserialize<UIItem>(JsonSerializer.Serialize(ui))!;

            Visibility(restored).Should().Equal(Visibility(ui));
        }
    }

    private static bool[] Visibility(UIItem ui) =>
    [
        ui.TriskoShowConnectionStatus,
        ui.TriskoShowConnectionResources,
        ui.TriskoShowConnectionConfiguration,
        ui.TriskoShowConnectionLogs
    ];
}
