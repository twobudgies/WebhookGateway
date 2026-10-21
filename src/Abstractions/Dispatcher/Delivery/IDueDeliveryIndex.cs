namespace WebhookGateway.Dispatcher.Delivery;

/// <summary>
/// Deliveries in status <see cref="WebhookGateway.Contracts.DeliveryStatus.Scheduled"/> whose wait is over 15 minutes
/// (docs/data-plane.md section 4, gsi3). The retry scheduler reads this and puts them back on the tier queue.
/// </summary>
public interface IDueDeliveryIndex
{
    /// <returns>Up to <paramref name="limit"/> scheduled deliveries whose next attempt time is at or before <paramref name="now"/>.</returns>
    ValueTask<IReadOnlyList<DeliveryStateRecord>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}
