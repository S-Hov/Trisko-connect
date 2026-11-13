using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public sealed class TriskoUserResourceServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "trisko-user-resources-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UpsertAsync_ShouldPersistNewResource()
    {
        var service = CreateService();
        var resource = CreateSsh("local-one", "Первый SSH");

        await service.UpsertAsync(resource, TestContext.Current.CancellationToken);
        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        loaded.Should().ContainSingle();
        loaded[0].Should().BeEquivalentTo(resource);
    }

    [Fact]
    public async Task UpsertAsync_ShouldReplaceResourceWithSameId()
    {
        var service = CreateService();
        await service.UpsertAsync(
            CreateSsh("managed-ssh", "Старое название"),
            TestContext.Current.CancellationToken);

        await service.UpsertAsync(
            CreateSsh("managed-ssh", "Новое название"),
            TestContext.Current.CancellationToken);
        var loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        loaded.Should().ContainSingle();
        loaded[0].DisplayName.Should().Be("Новое название");
    }

    [Fact]
    public void Merge_ShouldApplyUserOverrideAndKeepManualResource()
    {
        var managed = new[] { CreateSsh("managed-ssh", "Из комплекта") };
        var user = new[]
        {
            CreateSsh("managed-ssh", "Изменено пользователем"),
            CreateSsh("local-ssh", "Добавлено вручную")
        };

        var merged = TriskoUserResourceService.Merge(managed, user);

        merged.Should().HaveCount(2);
        merged[0].DisplayName.Should().Be("Изменено пользователем");
        merged[1].DisplayName.Should().Be("Добавлено вручную");
    }

    [Fact]
    public async Task HideManagedAsync_ShouldPersistTombstoneWithoutChangingManagedSource()
    {
        var service = CreateService();
        var managed = new[] { CreateSsh("managed-ssh", "Из комплекта") };

        await service.HideManagedAsync("managed-ssh", TestContext.Current.CancellationToken);
        var state = await service.LoadStateAsync(TestContext.Current.CancellationToken);
        var merged = TriskoUserResourceService.Merge(
            managed,
            state.Resources,
            state.DeletedManagedResourceIds);

        state.DeletedManagedResourceIds.Should().Contain("managed-ssh");
        merged.Should().BeEmpty();
        managed.Should().ContainSingle();
    }

    [Fact]
    public async Task ClearManagedChangesAsync_ShouldKeepManualResources()
    {
        var service = CreateService();
        await service.UpsertAsync(
            CreateSsh("managed-ssh", "Локальное изменение"),
            TestContext.Current.CancellationToken);
        await service.UpsertAsync(
            CreateSsh("local-ssh", "Добавлено вручную"),
            TestContext.Current.CancellationToken);
        await service.HideManagedAsync("managed-hidden", TestContext.Current.CancellationToken);

        await service.ClearManagedChangesAsync(
            ["managed-ssh", "managed-hidden"],
            TestContext.Current.CancellationToken);
        var state = await service.LoadStateAsync(TestContext.Current.CancellationToken);

        state.Resources.Should().ContainSingle(resource => resource.Id == "local-ssh");
        state.DeletedManagedResourceIds.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadStateAsync_OldStoreWithoutTombstones_ShouldMigrateInMemory()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "resources.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "resources": [
                {
                  "id": "local-ssh",
                  "type": "ssh",
                  "displayName": "Старый формат",
                  "host": "10.0.0.9",
                  "port": 22,
                  "userName": "operator",
                  "requiresVpn": true
                }
              ]
            }
            """,
            TestContext.Current.CancellationToken);
        var service = new TriskoUserResourceService(path);

        var state = await service.LoadStateAsync(TestContext.Current.CancellationToken);

        state.Resources.Should().ContainSingle(resource => resource.Id == "local-ssh");
        state.DeletedManagedResourceIds.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private TriskoUserResourceService CreateService()
    {
        Directory.CreateDirectory(_directory);
        return new TriskoUserResourceService(Path.Combine(_directory, "resources.json"));
    }

    private static TriskoResourceProfile CreateSsh(string id, string displayName) => new()
    {
        Id = id,
        Type = "ssh",
        DisplayName = displayName,
        Host = "ssh.corp.test",
        Port = 22,
        UserName = "operator",
        RequiresVpn = true
    };
}
