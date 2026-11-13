namespace ServiceLib.Trisko;

public sealed class TriskoConfigurationException : Exception
{
    public TriskoConfigurationException(string userMessage)
        : base(userMessage)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
