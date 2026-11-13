namespace ServiceLib.Geksa;

public sealed class GeksaEcpServerItemViewModel
{
    private readonly Func<GeksaEcpServerItemViewModel, Task> _remove;

    public GeksaEcpServerItemViewModel(
        GeksaVirtualHereHubConfiguration configuration,
        Func<GeksaEcpServerItemViewModel, Task> remove)
    {
        Configuration = configuration;
        _remove = remove;
        RemoveCommand = ReactiveCommand.CreateFromTask(() => _remove(this));
    }

    public GeksaVirtualHereHubConfiguration Configuration { get; }

    public string Host => Configuration.Host;

    public int Port => Configuration.Port;

    public string Endpoint => Configuration.Endpoint;

    public ReactiveCommand<RxVoid, RxVoid> RemoveCommand { get; }
}
