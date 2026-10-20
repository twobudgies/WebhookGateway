using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Retry;

namespace WebhookGateway.Dispatcher.Delivery;

public enum DeliveryAction
{
    /// <summary>Sent and acknowledged by the subscriber.</summary>
    Delivered,

    /// <summary>Failed; the next attempt is back on the queue with a delay.</summary>
    RetryQueued,

    /// <summary>Failed; the next attempt is more than 15 minutes away and waits in the scheduled index.</summary>
    RetryScheduled,

    /// <summary>Will not be retried (permanent failure, missing endpoint or payload).</summary>
    Failed,

    /// <summary>All attempts used.</summary>
    Exhausted,

    /// <summary>Waited longer than the maximum backlog age; not sent.</summary>
    Expired,

    /// <summary>A redelivered message for a delivery that already ended; nothing to do.</summary>
    Skipped,
}

/// <summary>
/// The decision logic for one <see cref="DeliveryMessage"/>: send it, then record or schedule according to the result.
/// Only non-success paths write state. Any exception (storage or queue error) propagates so the message is not deleted
/// and SQS redelivers it, which is safe because everything here is idempotent.
/// </summary>
public sealed class DeliveryProcessor(
    IEndpointStore endpoints,
    IPayloadResolver payloads,
    IWebhookSender sender,
    IDeliveryStateStore state,
    IRetryPublisher retries,
    RetryPolicy retryPolicy,
    TimeProvider clock,
    IOptions<DeliveryOptions> options)
{
    private readonly DeliveryOptions _options = options.Value;

    public async ValueTask<DeliveryAction> ProcessAsync(DeliveryMessage message, PlanTier tier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = clock.GetUtcNow();

        var existing = await state.GetAsync(message.TenantId, message.DeliveryId, cancellationToken).ConfigureAwait(false);
        if (existing is { IsFinal: true })
        {
            return DeliveryAction.Skipped;
        }

        if (now - message.NotBefore > _options.MaxBacklogAge)
        {
            await RecordAsync(message, DeliveryStatus.Expired, nextAttemptAt: null, statusCode: null, "expired", now, cancellationToken).ConfigureAwait(false);
            return DeliveryAction.Expired;
        }

        var target = await endpoints.GetAsync(message.TenantId, message.EndpointId, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            await RecordAsync(message, DeliveryStatus.Failed, null, null, "endpoint_unavailable", now, cancellationToken).ConfigureAwait(false);
            return DeliveryAction.Failed;
        }

        var payload = await payloads.ResolveAsync(message, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            await RecordAsync(message, DeliveryStatus.Failed, null, null, "payload_unavailable", now, cancellationToken).ConfigureAwait(false);
            return DeliveryAction.Failed;
        }

        var result = await sender.SendAsync(target, message, payload.Value, cancellationToken).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case SendOutcome.Succeeded:
                if (message.Attempt > 1)
                {
                    await state.DeleteAsync(message.TenantId, message.DeliveryId, cancellationToken).ConfigureAwait(false);
                }

                return DeliveryAction.Delivered;

            case SendOutcome.Failed:
                await RecordAsync(message, DeliveryStatus.Failed, null, result.StatusCode, result.Error, now, cancellationToken).ConfigureAwait(false);
                return DeliveryAction.Failed;

            default:
                return await HandleRetryAsync(message, tier, result, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<DeliveryAction> HandleRetryAsync(
        DeliveryMessage message,
        PlanTier tier,
        SendResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var delay = retryPolicy.NextDelay(tier, message.Attempt, result.RetryAfter);
        if (delay is null)
        {
            await RecordAsync(message, DeliveryStatus.Exhausted, null, result.StatusCode, result.Error, now, cancellationToken).ConfigureAwait(false);
            return DeliveryAction.Exhausted;
        }

        var nextAttemptAt = now + delay.Value;
        if (delay.Value > _options.MaxQueueDelay)
        {
            await RecordAsync(message, DeliveryStatus.Scheduled, nextAttemptAt, result.StatusCode, result.Error, now, cancellationToken).ConfigureAwait(false);
            return DeliveryAction.RetryScheduled;
        }

        // Row first, then the message: if publishing fails the message is redelivered and the row is simply rewritten.
        await RecordAsync(message, DeliveryStatus.RetryScheduled, nextAttemptAt, result.StatusCode, result.Error, now, cancellationToken).ConfigureAwait(false);
        await retries.PublishAsync(tier, NextAttempt(message, nextAttemptAt), delay.Value, cancellationToken).ConfigureAwait(false);
        return DeliveryAction.RetryQueued;
    }

    /// <summary>The message for the following attempt; the scheduler uses the same shape for long waits.</summary>
    public static DeliveryMessage NextAttempt(DeliveryMessage message, DateTimeOffset notBefore)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message with { Attempt = message.Attempt + 1, NotBefore = notBefore };
    }

    private ValueTask RecordAsync(
        DeliveryMessage message,
        DeliveryStatus status,
        DateTimeOffset? nextAttemptAt,
        int? statusCode,
        string? error,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        state.PutAsync(
            new DeliveryStateRecord(
                message.TenantId,
                message.DeliveryId,
                message.EndpointId,
                message.EventId,
                message.EventType,
                message.OccurredAt,
                status,
                message.Attempt,
                nextAttemptAt,
                statusCode,
                error,
                message.Payload,
                message.PayloadRef,
                message.CreatedAt,
                now),
            cancellationToken);
}
