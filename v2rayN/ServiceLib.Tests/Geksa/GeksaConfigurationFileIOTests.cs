using System.Text;
using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public sealed class GeksaConfigurationFileIOTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "gek-sa-config-io-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadLimitedAsync_OversizedStream_ShouldReject()
    {
        await using var stream = new MemoryStream(new byte[33]);

        var action = async () => await GeksaConfigurationFileIO.ReadLimitedAsync(
            stream,
            maxBytes: 32,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<GeksaConfigurationException>()
            .WithMessage("*больше 32 байт*");
    }

    [Fact]
    public async Task WriteTextAtomicallyAsync_ShouldReplaceLongerFileWithoutTrailingBytes()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "resource.json");
        await File.WriteAllTextAsync(
            path,
            "old content that is much longer",
            TestContext.Current.CancellationToken);

        await GeksaConfigurationFileIO.WriteTextAtomicallyAsync(
            path,
            "новый",
            TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Encoding.UTF8.GetString(bytes).Should().Be("новый");
        bytes.Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF]);
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeSuggestedFileName_ShouldRemoveWindowsUnsafeCharacters()
    {
        var result = GeksaConfigurationFileIO.SanitizeSuggestedFileName(
            "RDP: Финансы/Главный?",
            "connection");

        result.Should().NotContainAny(":", "/", "?");
        result.Should().Contain("Финансы");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
