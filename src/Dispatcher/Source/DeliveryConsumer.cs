using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Dispatcher.Delivery;

namespace WebhookGateway.Dispatcher.Source;

/// <summary>
/// One receive loop per plan tier. Each message is processed concurrently (bounded per tier) and acknowledged only
/// after <see cref="IDeliveryProcessor"/> finished; a failure leaves it on the queue for redelivery, which is safe
/// because processing is idempotent (docs/data-plane.md section 6).
/// </summary>
public sealed partial class DeliveryConsumer(
    IDeliverySource source,
    IDeliveryProcessor processor,
    IOptions<DeliveryConsumerOptions> options,
    ILogger<DeliveryConsumer> logger) : BackgroundService
{
    private readonly DeliveryConsumerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, _options.MaxConcurrencyPerTier);
        await Task.WhenAll(Enum.GetValues<PlanTier>().Select(tier => RunTierAsync(tier, stoppingToken))).ConfigureAwait(false);
    }

    /// <summary>Handles one receive cycle for a tier and waits for its messages. Exposed so tests can drive it.</summary>
    public async ValueTask<int> RunOnceAsync(PlanTier tier, CancellationToken cancellationToken)
    {
        var received = await source.ReceiveAsync(tier, _options.BatchSize, cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(received.Select(r => HandleAsync(tier, r, cancellationToken))).ConfigureAwait(false);
        return received.Count;
    }

    private async Task RunTierAsync(PlanTier tier, CancellationToken stoppingToken)
    {
        using var slots = new SemaphoreSlim(_options.MaxConcurrencyPerTier);
        var running = new List<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                slots.Release();
                var received = await source.ReceiveAsync(tier, Math.Min(_options.BatchSize, _options.MaxConcurrencyPerTier), stoppingToken).ConfigureAwait(false);
                foreach (var delivery in received)
                {
                    await slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                    running.Add(RunSlotAsync(tier, delivery, slots, stoppingToken));
                }

                running.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive any single failure; unacknowledged messages are redelivered.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogReceiveFailed(logger, ex, tier);
                await Task.Delay(_options.FailureBackoff, stoppingToken).ConfigureAwait(false);
            }
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    private async Task RunSlotAsync(PlanTier tier, ReceivedDelivery delivery, SemaphoreSlim slots, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(tier, delivery, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task HandleAsync(PlanTier tier, ReceivedDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            await processor.ProcessAsync(delivery.Message, tier, cancellationToken).ConfigureAwait(false);
            await source.AcknowledgeAsync(tier, delivery, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: leave the message unacknowledged; it is redelivered.
        }
#pragma warning disable CA1031 // One bad delivery must not stop the others; it stays on the queue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(logger, ex, delivery.Message.TenantId.Value, delivery.Message.DeliveryId.Value, delivery.Message.Attempt, tier);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivery consumer started ({MaxConcurrency} concurrent deliveries per tier)")]
    private static partial void LogStarted(ILogger logger, int maxConcurrency);

    [LoggerMessage(Level = LogLevel.Error, Message = "Receiving from the {Tier} delivery queue failed; retrying")]
    private static partial void LogReceiveFailed(ILogger logger, Exception exception, PlanTier tier);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivery {DeliveryId} of tenant {TenantId} (attempt {Attempt}, tier {Tier}) could not be processed; it stays on the queue and will be redelivered")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, string tenantId, string deliveryId, int attempt, PlanTier tier);
}
