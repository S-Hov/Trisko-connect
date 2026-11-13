using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoCorporateConfigurationCodecTests
{
    [Fact]
    public void Parse_ValidConfiguration_ShouldReturnTypedConfiguration()
    {
        var configuration = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(CreateJson()));

        configuration.SchemaVersion.Should().Be(1);
        configuration.ConfigurationVersion.Should().Be("pilot-2026.08");
        configuration.Revision.Should().Be(3);
        configuration.Vpn.Should().BeNull();
        configuration.Resources.Should().HaveCount(3);
        configuration.Resources.Select(resource => resource.Type)
            .Should().ContainInOrder("rdp", "ssh", "smb");
    }

    [Fact]
    public void Parse_UnknownProperty_ShouldFailClosed()
    {
        var json = CreateJson().Replace(
            "\"revision\": 3,",
            "\"revision\": 3, \"unexpected\": true,");

        var action = () => TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*неверный JSON*");
    }

    [Fact]
    public void Parse_LegacyVpnSection_ShouldNotControlResourceImport()
    {
        var root = JsonNode.Parse(CreateJson())!.AsObject();
        root["vpn"] = new JsonObject
        {
            ["displayName"] = "Старый профиль",
            ["shareUri"] = "vless://legacy-value",
            ["enableTun"] = false
        };

        var configuration = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(root.ToJsonString()));

        configuration.Resources.Should().HaveCount(3);
        configuration.Vpn.Should().BeNull();
    }

    [Fact]
    public void Parse_DuplicateResourceId_ShouldRejectConfiguration()
    {
        var json = CreateJson().Replace("\"id\": \"ssh-admin\"", "\"id\": \"rdp-office\"");

        var action = () => TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*повторяется*");
    }

    [Theory]
    [InlineData("server;shutdown.exe")]
    [InlineData("server/path")]
    [InlineData("server name")]
    [InlineData("server:22")]
    public void Parse_UnsafeHost_ShouldRejectConfiguration(string host)
    {
        var json = CreateJson().Replace("ssh.corp.test", host);

        var action = () => TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*Адрес ресурса*");
    }

    [Theory]
    [InlineData("../finance")]
    [InlineData("finance/../admin")]
    [InlineData("C:/Windows")]
    public void Parse_UnsafeSmbPath_ShouldRejectConfiguration(string path)
    {
        var json = CreateJson().Replace("departments/finance", path);

        var action = () => TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*неверный путь*");
    }

    [Theory]
    [InlineData("C:")]
    [InlineData("AA")]
    [InlineData("Z:\\")]
    public void Parse_InvalidSmbDriveLetter_ShouldRejectConfiguration(string driveLetter)
    {
        var json = CreateJson().Replace(
            "\"path\": \"departments/finance\",",
            $"\"path\": \"departments/finance\", \"driveLetter\": \"{driveLetter.Replace("\\", "\\\\")}\",");

        var action = () => TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*неверную букву диска*");
    }

    [Theory]
    [InlineData("resources", "Отсутствует секция resources")]
    public void Parse_NullRequiredSection_ShouldReturnSafeError(
        string section,
        string expectedMessage)
    {
        var root = JsonNode.Parse(CreateJson())!.AsObject();
        root[section] = null;
        var json = root.ToJsonString();

        var action = () => TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void ComputeSourceHash_SameContent_ShouldBeStable()
    {
        var first = TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(CreateJson()));
        var second = TriskoCorporateConfigurationCodec.Parse(Encoding.UTF8.GetBytes(CreateJson()));

        TriskoCorporateConfigurationCodec.ComputeSourceHash(first)
            .Should().Be(TriskoCorporateConfigurationCodec.ComputeSourceHash(second))
            .And.HaveLength(64);
    }

    [Fact]
    public void ComputeSourceHash_LegacyVpnSection_ShouldNotAffectResources()
    {
        var resourcesOnly = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(CreateJson()));
        var withLegacyVpn = new TriskoCorporateConfiguration
        {
            SchemaVersion = resourcesOnly.SchemaVersion,
            ConfigurationVersion = resourcesOnly.ConfigurationVersion,
            Revision = resourcesOnly.Revision,
            Resources = resourcesOnly.Resources,
            Vpn = new TriskoVpnProfileConfiguration
            {
                DisplayName = "Legacy",
                ShareUri = "vless://must-be-ignored"
            }
        };

        TriskoCorporateConfigurationCodec.ComputeSourceHash(withLegacyVpn)
            .Should().Be(TriskoCorporateConfigurationCodec.ComputeSourceHash(resourcesOnly));
    }

    [Fact]
    public void MissingSmbDriveLetter_ShouldStayAbsentFromCanonicalModel()
    {
        var configuration = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(CreateJson()));

        JsonSerializer.Serialize(configuration).Should().NotContain("DriveLetter");
    }

    [Fact]
    public void ConfiguredSmbDriveLetter_ShouldChangeSourceHash()
    {
        var automatic = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(CreateJson()));
        var configuredJson = CreateJson().Replace(
            "\"path\": \"departments/finance\",",
            "\"path\": \"departments/finance\", \"driveLetter\": \"Z:\",");
        var configured = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(configuredJson));

        TriskoCorporateConfigurationCodec.ComputeSourceHash(automatic)
            .Should().NotBe(TriskoCorporateConfigurationCodec.ComputeSourceHash(configured));
    }

    [Fact]
    public void Parse_PrivatePilotConfiguration_WhenPathIsProvided_ShouldValidateIt()
    {
        var path = Environment.GetEnvironmentVariable("TRISKO_PILOT_GAC3_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var configuration = TriskoCorporateConfigurationCodec.Parse(File.ReadAllBytes(path));

        configuration.Resources.Should().NotBeEmpty();
    }

    [Fact]
    public void ParseResourceProfile_ValidSingleResource_ShouldRoundTrip()
    {
        var original = new TriskoResourceProfile
        {
            Id = "ssh-export",
            Type = "ssh",
            DisplayName = "SSH export",
            Host = "10.20.30.40",
            Port = 22,
            UserName = "operator",
            RequiresVpn = true
        };

        var json = TriskoCorporateConfigurationCodec.SerializeResourceProfile(original);
        var parsed = TriskoCorporateConfigurationCodec.ParseResourceProfile(
            Encoding.UTF8.GetBytes(json));

        parsed.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void ParseResourceProfile_FullConfiguration_ShouldGiveScopeHint()
    {
        var action = () => TriskoCorporateConfigurationCodec.ParseResourceProfile(
            Encoding.UTF8.GetBytes(CreateJson()));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*полный корпоративный комплект*Корпоративная конфигурация*");
    }

    [Fact]
    public void ParseResourceProfile_ResourceArray_ShouldRejectInsteadOfTakingFirst()
    {
        const string json = """
        [{"id":"ssh-one","type":"ssh","displayName":"SSH","host":"10.0.0.1","port":22,"userName":"admin","requiresVpn":true}]
        """;

        var action = () => TriskoCorporateConfigurationCodec.ParseResourceProfile(
            Encoding.UTF8.GetBytes(json));

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*одно подключение*JSON-объекта*");
    }

    [Fact]
    public void SerializeConfiguration_ResourceOnly_ShouldOmitVpn()
    {
        var configuration = new TriskoCorporateConfiguration
        {
            SchemaVersion = 1,
            ConfigurationVersion = "export-test",
            Revision = 1,
            Resources = []
        };

        var json = TriskoCorporateConfigurationCodec.SerializeConfiguration(configuration);

        json.Should().NotContain("\"vpn\"");
    }

    [Fact]
    public void SerializeConfiguration_LegacyVpnValue_ShouldStillOmitVpn()
    {
        var configuration = new TriskoCorporateConfiguration
        {
            SchemaVersion = 1,
            ConfigurationVersion = "export-test",
            Revision = 1,
            Vpn = new TriskoVpnProfileConfiguration
            {
                DisplayName = "Legacy",
                ShareUri = "vless://legacy"
            },
            Resources = []
        };

        var json = TriskoCorporateConfigurationCodec.SerializeConfiguration(configuration);

        json.Should().NotContain("\"vpn\"");
    }

    [Fact]
    public void CreatePreview_ShouldReportResourceDiffAndLocalOverrides()
    {
        var currentResource = new TriskoResourceProfile
        {
            Id = "ssh-admin",
            Type = "ssh",
            DisplayName = "Старое SSH",
            Host = "10.0.0.1",
            Port = 22,
            UserName = "admin"
        };
        var candidate = TriskoCorporateConfigurationCodec.Parse(
            Encoding.UTF8.GetBytes(CreateJson()));
        var current = new TriskoActiveConfiguration(
            1,
            "old",
            2,
            new string('A', 64),
            "trisko-corporate-v3",
            "VPN",
            [currentResource]);
        var userState = new TriskoUserResourceState(
            [currentResource],
            new HashSet<string>(["rdp-office"], StringComparer.OrdinalIgnoreCase));

        var preview = TriskoCorporateConfigurationService.CreatePreview(
            "new.gac3",
            candidate,
            TriskoCorporateConfigurationCodec.ComputeSourceHash(candidate),
            current,
            userState);

        preview.AddedCount.Should().Be(2);
        preview.UpdatedCount.Should().Be(1);
        preview.RemovedCount.Should().Be(0);
        preview.LocalOverrideCount.Should().Be(1);
        preview.LocallyHiddenCount.Should().Be(1);
        preview.RdpCount.Should().Be(1);
        preview.SshCount.Should().Be(1);
        preview.SmbCount.Should().Be(1);
    }

    private static string CreateJson()
    {
        return $$"""
        {
          "schemaVersion": 1,
          "configurationVersion": "pilot-2026.08",
          "revision": 3,
          "resources": [
            {
              "id": "rdp-office",
              "type": "rdp",
              "displayName": "Рабочий стол",
              "host": "rdp.corp.test",
              "port": 3389,
              "userName": "CORP\\user",
              "requiresVpn": true,
              "fullScreen": true,
              "multiMonitor": false
            },
            {
              "id": "ssh-admin",
              "type": "ssh",
              "displayName": "Администрирование",
              "host": "ssh.corp.test",
              "port": 22,
              "userName": "operator",
              "requiresVpn": true
            },
            {
              "id": "smb-finance",
              "type": "smb",
              "displayName": "Финансы",
              "host": "files.corp.test",
              "port": 445,
              "path": "departments/finance",
              "requiresVpn": true
            }
          ]
        }
        """;
    }
}
