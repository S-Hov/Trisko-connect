namespace ServiceLib.Geksa;

public sealed class GeksaConfigurationException : Exception
{
    public GeksaConfigurationException(string userMessage)
        : base(userMessage)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
