using AwesomeAssertions;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoSidebarNavigationVisibilityTests
{
    [Fact]
    public void UIItem_NavigationVisibilityDefaults_ShouldShowSupportedSections()
    {
        var uiItem = new UIItem();

        uiItem.TriskoShowConnectionNav.Should().BeTrue();
        uiItem.TriskoShowRdpNav.Should().BeTrue();
        uiItem.TriskoShowSmbNav.Should().BeTrue();
        uiItem.TriskoShowSshNav.Should().BeTrue();
        uiItem.TriskoShowEcpNav.Should().BeTrue();
        uiItem.TriskoShowSettingsNav.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true, true, true, true, true, 6)]
    [InlineData(true, false, false, false, false, false, 1)]
    [InlineData(false, false, false, false, false, false, 0)]
    public void VisibleSectionsCounter_ShouldCalculateCorrectly(
        bool conn, bool rdp, bool smb, bool ssh, bool ecp, bool settings, int expectedCount)
    {
        int count = 0;
        if (conn) count++;
        if (rdp) count++;
        if (smb) count++;
        if (ssh) count++;
        if (ecp) count++;
        if (settings) count++;

        count.Should().Be(expectedCount);
    }
}
