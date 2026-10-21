namespace WebhookGateway.Dispatcher.Outbound;

/// <summary>Raised at connect time when a subscriber host resolves only to addresses that policy forbids.</summary>
public sealed class BlockedAddressException : Exception
{
    public BlockedAddressException()
    {
    }

    public BlockedAddressException(string message)
        : base(message)
    {
    }

    public BlockedAddressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
