using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoHostsDocumentTests
{
    private static readonly TriskoOneCSystemTarget Target = new(
        "triskodata",
        "192.168.3.222");

    [Fact]
    public void Configure_ShouldAddOwnedBlockAndPreserveUnrelatedEntries()
    {
        const string source =
            "# Windows hosts\r\n" +
            "127.0.0.1 localhost\r\n" +
            "10.10.10.10 internal.example # keep me\r\n";

        var result = TriskoHostsDocument.Configure(
            source,
            Target,
            replaceConflictingEntries: false);

        result.Should().Contain("127.0.0.1 localhost");
        result.Should().Contain("10.10.10.10 internal.example # keep me");
        result.Should().Contain(TriskoHostsDocument.BeginMarker);
        result.Should().Contain("192.168.3.222\ttriskodata");
        result.Should().Contain(TriskoHostsDocument.EndMarker);

        var inspection = TriskoHostsDocument.Inspect(result, Target);
        inspection.State.Should().Be(TriskoOneCSetupState.Ready);
        inspection.HasManagedMapping.Should().BeTrue();
    }

    [Fact]
    public void Configure_ShouldRequireConfirmationForSameNameOrAddress()
    {
        const string source =
            "192.168.3.100 triskodata\n" +
            "192.168.3.222 old-triskodata\n";

        var act = () => TriskoHostsDocument.Configure(
            source,
            Target,
            replaceConflictingEntries: false);

        act.Should().Throw<InvalidOperationException>();

        var inspection = TriskoHostsDocument.Inspect(source, Target);
        inspection.State.Should().Be(TriskoOneCSetupState.Warning);
        inspection.ConflictingEntries.Should().HaveCount(2);
        inspection.RelatedEntries.Should().Contain(entry => entry.Contains("Строка 1"));
        inspection.RelatedEntries.Should().Contain(entry => entry.Contains("Строка 2"));
    }

    [Fact]
    public void Configure_WithConfirmation_ShouldReplaceOnlyRelatedLines()
    {
        const string source =
            "127.0.0.1 localhost\n" +
            "192.168.3.100 triskodata\n" +
            "192.168.3.222 old-triskodata\n" +
            "10.20.30.40 keep-this\n";

        var result = TriskoHostsDocument.Configure(
            source,
            Target,
            replaceConflictingEntries: true);

        result.Should().Contain("127.0.0.1 localhost");
        result.Should().Contain("10.20.30.40 keep-this");
        result.Should().NotContain("192.168.3.100 triskodata");
        result.Should().NotContain("192.168.3.222 old-triskodata");
        result.Split("192.168.3.222", StringSplitOptions.None).Should().HaveCount(2);
    }

    [Fact]
    public void Configure_ShouldBeIdempotent()
    {
        var first = TriskoHostsDocument.Configure(
            "127.0.0.1 localhost\n",
            Target,
            replaceConflictingEntries: false);
        var second = TriskoHostsDocument.Configure(
            first,
            Target,
            replaceConflictingEntries: false);

        second.Should().Be(first);
    }

    [Fact]
    public void RemoveManagedBlock_ShouldLeaveExternalEntriesUntouched()
    {
        const string external =
            "127.0.0.1 localhost\r\n" +
            "10.20.30.40 keep-this\r\n";
        var configured = TriskoHostsDocument.Configure(
            external,
            Target,
            replaceConflictingEntries: false);

        var result = TriskoHostsDocument.RemoveManagedBlock(configured);

        result.Should().Be(external);
    }

    [Theory]
    [InlineData("# BEGIN Trisko connect managed 1C\n192.168.3.222 triskodata\n")]
    [InlineData("# END Trisko connect managed 1C\n")]
    [InlineData(
        "# BEGIN Trisko connect managed 1C\n" +
        "# END Trisko connect managed 1C\n" +
        "# BEGIN Trisko connect managed 1C\n" +
        "# END Trisko connect managed 1C\n")]
    public void Inspect_ShouldReportMalformedManagedBlocks(string source)
    {
        var inspection = TriskoHostsDocument.Inspect(source, Target);

        inspection.State.Should().Be(TriskoOneCSetupState.Error);
        inspection.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(" TRISKODATA. ", "192.168.003.222", "triskodata", "192.168.3.222")]
    [InlineData("erp.corp.local", "10.20.30.40", "erp.corp.local", "10.20.30.40")]
    public void TargetValidator_ShouldNormalizeEditableValues(
        string name,
        string address,
        string expectedName,
        string expectedAddress)
    {
        var result = TriskoOneCSystemTargetValidator.Normalize(new(name, address));

        result.ServerName.Should().Be(expectedName);
        result.ServerAddress.Should().Be(expectedAddress);
    }

    [Theory]
    [InlineData("bad name", "192.168.3.222")]
    [InlineData("triskodata", "300.1.1.1")]
    [InlineData("", "192.168.3.222")]
    public void TargetValidator_ShouldRejectInvalidValues(string name, string address)
    {
        var act = () => TriskoOneCSystemTargetValidator.Normalize(new(name, address));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DecodeHostsFile_ShouldPreserveUtf16EncodingAndPreamble()
    {
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: true,
            throwOnInvalidBytes: true);
        const string content = "127.0.0.1 localhost\r\n# комментарий\r\n";
        var source = encoding.GetPreamble()
            .Concat(encoding.GetBytes(content))
            .ToArray();

        var snapshot = WindowsTriskoOneCSystemService.DecodeHostsFile(source);
        var roundTrip = snapshot.Preamble
            .Concat(snapshot.Encoding.GetBytes(snapshot.Content))
            .ToArray();

        snapshot.Content.Should().Be(content);
        roundTrip.Should().Equal(source);
    }

    [Fact]
    public void DecodeHostsFile_ShouldPreserveLegacyAnsiBytesLosslessly()
    {
        byte[] source =
        [
            0x31, 0x32, 0x37, 0x2E, 0x30, 0x2E, 0x30, 0x2E, 0x31, 0x20,
            0x6C, 0x6F, 0x63, 0x61, 0x6C, 0x68, 0x6F, 0x73, 0x74, 0x20,
            0x23, 0x20, 0xCF, 0xF0, 0xE8, 0xEC, 0xE5, 0xF0, 0x0D, 0x0A
        ];

        var snapshot = WindowsTriskoOneCSystemService.DecodeHostsFile(source);
        var roundTrip = snapshot.Preamble
            .Concat(snapshot.Encoding.GetBytes(snapshot.Content))
            .ToArray();

        roundTrip.Should().Equal(source);
    }
}
