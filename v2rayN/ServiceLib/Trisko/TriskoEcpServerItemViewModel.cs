namespace ServiceLib.Trisko;

public sealed class TriskoEcpServerItemViewModel
{
    private readonly Func<TriskoEcpServerItemViewModel, Task> _remove;

    public TriskoEcpServerItemViewModel(
        TriskoVirtualHereHubConfiguration configuration,
        Func<TriskoEcpServerItemViewModel, Task> remove)
    {
        Configuration = configuration;
        _remove = remove;
        RemoveCommand = ReactiveCommand.CreateFromTask(() => _remove(this));
    }

    public TriskoVirtualHereHubConfiguration Configuration { get; }

    public string Host => Configuration.Host;

    public int Port => Configuration.Port;

    public string Endpoint => Configuration.Endpoint;

    public ReactiveCommand<RxVoid, RxVoid> RemoveCommand { get; }
}
