namespace ServiceLib.Geksa;

public sealed class TriskoSubscriptionItem
{
    public TriskoSubscriptionItem(SubItem item, GeksaShellViewModel owner)
    {
        Id = item.Id;
        Name = item.Remarks;
        Host = Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
        Usage = GeksaSubscriptionUsagePresentation.Create([item]);
        EditCommand = ReactiveCommand.CreateFromTask(() => owner.EditSubscriptionAsync(item));
        UpdateCommand = ReactiveCommand.CreateFromTask(() => owner.UpdateSubscriptionAsync(item));
        DeleteCommand = ReactiveCommand.CreateFromTask(() => owner.DeleteSubscriptionAsync(item));
    }

    public string Id { get; }
    public string Name { get; }
    public string Host { get; }
    public GeksaSubscriptionUsagePresentation Usage { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UpdateCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
}

public partial class GeksaShellViewModel
{
    public async Task EditSubscriptionAsync(SubItem subscription)
    {
        if (IsBusy || IsVpnSubscriptionBusy || _controller.Snapshot.IsCoreRunning) return;
        var request = await ConfigureVpnSubscriptionInteraction.Handle(
            new GeksaVpnSubscriptionEditContext(subscription.Url, subscription.Remarks));
        if (request is not null)
            await ConfigureVpnSubscriptionAsync(false, request, subscription.Id);
    }

    public async Task UpdateSubscriptionAsync(SubItem subscription)
    {
        if (IsBusy || IsVpnSubscriptionBusy || _controller.Snapshot.IsCoreRunning) return;
        try
        {
            await RunScheduledSubscriptionUpdateAsync(async () =>
            {
                if (!await UpdateVpnSubscriptionsCoreAsync(subscription.Id))
                    throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось обновить подписку."));
            });
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(UpdateSubscriptionAsync), ex);
            FailureMessage = TriskoLocalization.Text("Не удалось обновить подписку.");
        }
    }

    public async Task DeleteSubscriptionAsync(SubItem subscription)
    {
        if (IsBusy || IsVpnSubscriptionBusy || _controller.Snapshot.IsCoreRunning) return;
        if (!await ConfirmOneCSystemChangeInteraction.Handle(TriskoLocalization.Text("Удалить подписку и её серверы? Одиночные подключения сохранятся."))) return;
        IsVpnSubscriptionBusy = true;
        try
        {
            if (await ConfigHandler.DeleteSubItem(_appConfig, subscription.Id) != 0)
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось удалить подписку."));
            if (_appConfig.SubIndexId == subscription.Id) _appConfig.SubIndexId = string.Empty;
            await ConfigHandler.SaveConfig(_appConfig);
            await RefreshVpnProfilesAsync(CancellationToken.None);
            await RefreshVpnSubscriptionStatusAsync();
            await _controller.RefreshAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(DeleteSubscriptionAsync), ex);
            FailureMessage = TriskoLocalization.Text("Не удалось удалить подписку.");
        }
        finally { IsVpnSubscriptionBusy = false; }
    }
}
