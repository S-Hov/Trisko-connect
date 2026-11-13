using ReactiveUI.Builder;
using ReactiveUI.Primitives.Signals;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaNotificationCenterTests
{
    static GeksaNotificationCenterTests()
    {
        try { RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp(); }
        catch { }
    }

    [Fact]
    public async Task DuplicateErrorKeepsFullDetailsAndCanBeDismissed()
    {
        using var center = new GeksaNotificationCenter();
        center.Show("Ошибка", "Не удалось подключиться", GeksaNotificationKind.Error, "Full exception\nInner exception");
        center.Show("Ошибка", "Не удалось подключиться", GeksaNotificationKind.Error);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var item = Assert.Single(center.Items);
        Assert.True(item.IsError);
        Assert.Contains("Inner exception", item.Details);
        await center.DismissCommand.Execute(item);
        Assert.Empty(center.Items);
    }

    [Fact]
    public async Task QueueIsBoundedAndPreservesAction()
    {
        using var center = new GeksaNotificationCenter();
        using var action = ReactiveCommand.Create(() => { });
        for (int i = 0; i < 6; i++)
            center.Show("Готово", $"Действие {i}", GeksaNotificationKind.Success,
                actionText: "Открыть", action: action);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(4, center.Items.Count);
        Assert.All(center.Items, item => { Assert.True(item.HasAction); Assert.Same(action, item.ActionCommand); });
    }

    [Fact]
    public async Task DisposedCenterIgnoresNewMessages()
    {
        var center = new GeksaNotificationCenter();
        center.Dispose();
        center.Show("Ошибка", "Сообщение после закрытия", GeksaNotificationKind.Error);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(center.Items);
    }

    [Fact]
    public async Task SuccessExpiresWhileErrorRemainsAvailableForCopying()
    {
        using var center = new GeksaNotificationCenter();
        center.Show("Успех", "Подключение установлено", GeksaNotificationKind.Success);
        center.Show("Ошибка", "Полная ошибка", GeksaNotificationKind.Error, "Details for clipboard");
        await Task.Delay(TimeSpan.FromSeconds(7.5), TestContext.Current.CancellationToken);

        var remaining = Assert.Single(center.Items);
        Assert.True(remaining.IsError);
        Assert.Equal("Details for clipboard", remaining.Details);
    }
}
