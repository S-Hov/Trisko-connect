namespace ServiceLib.Geksa;

public sealed class GeksaVpnOperationException : Exception
{
    public GeksaVpnOperationException(
        string code,
        string userMessage,
        bool retryable = true,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        Code = code;
        UserMessage = userMessage;
        Retryable = retryable;
    }

    public string Code { get; }

    public string UserMessage { get; }

    public bool Retryable { get; }
}
