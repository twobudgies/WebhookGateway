using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Retry;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class RetrySchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDeliveryStateStore _state = new();
    private readonly RecordingPublisher _publisher = new();

    private RetryScheduler Scheduler() =>
        new(_state, _state, _publisher, new FakeTimeProvider(Now), Options.Create(new RetrySchedulerOptions()), NullLogger<RetryScheduler>.Instance);

    private DeliveryStateRecord Put(DeliveryStatus status, DateTimeOffset next, PlanTier tier = PlanTier.Advanced, int attempt = 3)
    {
        var eventId = EventId.New();
        var endpointId = EndpointId.New();
        var record = new DeliveryStateRecord(
            TenantId.New(), DeliveryId.For(eventId, endpointId), endpointId, eventId, "order.created", Now.AddHours(-3), status, attempt,
            next, 503, "http_503", JsonDocument.Parse("{}").RootElement.Clone(), null, Now.AddHours(-2), Now.AddHours(-1), tier);
        _state.PutAsync(record, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return record;
    }

    [Fact]
    public async Task A_due_delivery_goes_back_on_its_tier_queue_as_the_next_attempt_and_leaves_the_index()
    {
        var record = Put(DeliveryStatus.Scheduled, Now.AddMinutes(-1));

        var moved = await Scheduler().RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, moved);
        var (tier, message) = Assert.Single(_publisher.Published);
        Assert.Equal(PlanTier.Advanced, tier);
        Assert.Equal(record.DeliveryId, message.DeliveryId);
        Assert.Equal(4, message.Attempt);
        Assert.Equal(DeliveryStatus.RetryScheduled, _state.Rows.Single().Status);
        Assert.Equal(0, await Scheduler().RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_delivery_that_is_not_due_yet_stays()
    {
        Put(DeliveryStatus.Scheduled, Now.AddHours(2));

        Assert.Equal(0, await Scheduler().RunOnceAsync(CancellationToken.None));
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task A_publish_failure_leaves_the_row_scheduled_for_the_next_poll()
    {
        Put(DeliveryStatus.Scheduled, Now.AddMinutes(-1));
        _publisher.Fail = true;

        var moved = await Scheduler().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, moved);
        Assert.Equal(DeliveryStatus.Scheduled, _state.Rows.Single().Status);
    }

    private sealed class RecordingPublisher : IRetryPublisher
    {
        public List<(PlanTier Tier, DeliveryMessage Message)> Published { get; } = [];

        public bool Fail { get; set; }

        public ValueTask PublishAsync(PlanTier tier, DeliveryMessage nextAttempt, TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("queue down");
            }

            Published.Add((tier, nextAttempt));
            return ValueTask.CompletedTask;
        }
    }
}
