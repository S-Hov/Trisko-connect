using ServiceLib.Models.Configs;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoStartupDefaultsTests
{
    [Fact]
    public void ANewProfileStartsVirtualHereOnlyOnRequest()
    {
        var settings = new UIItem();
        TriskoStartupDefaults.Apply(settings);
        Assert.False(settings.TriskoAutoStartVirtualHereOnLaunch);
        Assert.True(settings.TriskoVirtualHereStartupPreferenceInitialized);
    }

    [Fact]
    public void TheInheritedEnabledFlagIsResetOnce()
    {
        var settings = new UIItem { TriskoAutoStartVirtualHereOnLaunch = true };
        TriskoStartupDefaults.Apply(settings);
        Assert.False(settings.TriskoAutoStartVirtualHereOnLaunch);
        settings.TriskoAutoStartVirtualHereOnLaunch = true;
        TriskoStartupDefaults.Apply(settings);
        Assert.True(settings.TriskoAutoStartVirtualHereOnLaunch);
    }

    [Fact]
    public void AnExplicitChoiceMadeAfterTheMigrationIsPreserved()
    {
        var settings = new UIItem
        {
            TriskoAutoStartVirtualHereOnLaunch = true,
            TriskoVirtualHereStartupPreferenceInitialized = true
        };
        TriskoStartupDefaults.Apply(settings);
        Assert.True(settings.TriskoAutoStartVirtualHereOnLaunch);
    }
}
