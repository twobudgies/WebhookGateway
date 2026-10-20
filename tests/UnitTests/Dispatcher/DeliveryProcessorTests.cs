using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Contracts.Messages;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Outbound;
using WebhookGateway.Dispatcher.Retry;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class DeliveryProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonElement Payload = JsonDocument.Parse("""{"orderId":42}""").RootElement.Clone();

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryEndpointStore _endpoints = new();
    private readonly InMemoryDeliveryStateStore _state = new();
    private readonly ScriptedSender _sender = new();
    private readonly RecordingRetryPublisher _retries = new();
    private readonly DictionaryPayloadResolver _payloads = new();
    private readonly TenantId _tenant = TenantId.New();
    private readonly EndpointTarget _target;

    public DeliveryProcessorTests()
    {
        _target = new EndpointTarget(EndpointId.New(), new Uri("https://subscriber.example.com/hook"), [SigningSecret.Generate()]);
        _endpoints.Set(_tenant, _target);
    }

    private DeliveryProcessor Processor(double jitter = 1.0) =>
        new(_endpoints, _payloads, _sender, _state, _retries, new RetryPolicy(() => jitter), _time, Options.Create(new DeliveryOptions()));

    private DeliveryMessage Message(int attempt = 1, DateTimeOffset? notBefore = null, JsonElement? payload = null, string? payloadRef = null)
    {
        var eventId = EventId.New();
        return new DeliveryMessage(
            DeliveryMessage.CurrentSchemaVersion, _tenant, eventId, DeliveryId.For(eventId, _target.EndpointId), _target.EndpointId,
            "order.created", Now.AddMinutes(-5), attempt, Now.AddMinutes(-1), notBefore ?? Now, payload ?? (payloadRef is null ? Payload : null), payloadRef);
    }

    private static SendResult Ok() => new(SendOutcome.Succeeded, 200, 50, null);

    private static SendResult Retry(int status = 503, TimeSpan? retryAfter = null) => new(SendOutcome.Retry, status, 50, $"http_{status}", retryAfter);

    [Fact]
    public async Task A_successful_first_attempt_leaves_no_state_and_no_retry()
    {
        _sender.Enqueue(Ok());

        var action = await Processor().ProcessAsync(Message(), PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Delivered, action);
        Assert.Empty(_state.Rows);
        Assert.Empty(_retries.Published);
    }

    [Fact]
    public async Task Success_after_a_retry_removes_the_failure_row()
    {
        var first = Message();
        _sender.Enqueue(Retry());
        await Processor().ProcessAsync(first, PlanTier.Basic, CancellationToken.None);
        Assert.Single(_state.Rows);
        _sender.Enqueue(Ok());

        var action = await Processor().ProcessAsync(_retries.Published.Single().Message, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Delivered, action);
        Assert.Empty(_state.Rows);
    }

    [Fact]
    public async Task A_retryable_failure_requeues_the_next_attempt_with_the_backoff_delay()
    {
        _sender.Enqueue(Retry(503));
        var message = Message();

        var action = await Processor().ProcessAsync(message, PlanTier.Advanced, CancellationToken.None);

        Assert.Equal(DeliveryAction.RetryQueued, action);
        var (tier, next, delay) = Assert.Single(_retries.Published);
        Assert.Equal(PlanTier.Advanced, tier);
        Assert.Equal(TimeSpan.FromSeconds(30), delay);
        Assert.Equal(2, next.Attempt);
        Assert.Equal(Now + delay, next.NotBefore);
        Assert.Equal(message.DeliveryId, next.DeliveryId);
        var row = Assert.Single(_state.Rows);
        Assert.Equal(DeliveryStatus.RetryScheduled, row.Status);
        Assert.Equal(503, row.LastStatusCode);
        Assert.Equal(Now + delay, row.NextAttemptAt);
    }

    [Fact]
    public async Task A_long_wait_is_parked_in_the_scheduled_index_instead_of_the_queue()
    {
        _sender.Enqueue(Retry());

        var action = await Processor().ProcessAsync(Message(attempt: 4), PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.RetryScheduled, action);
        Assert.Empty(_retries.Published);
        var row = Assert.Single(_state.Rows);
        Assert.Equal(DeliveryStatus.Scheduled, row.Status);
        Assert.Equal(Now + TimeSpan.FromMinutes(30), row.NextAttemptAt);
    }

    [Fact]
    public async Task Retry_after_from_the_subscriber_lengthens_the_wait()
    {
        _sender.Enqueue(Retry(429, TimeSpan.FromMinutes(10)));

        await Processor().ProcessAsync(Message(), PlanTier.Basic, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(10), Assert.Single(_retries.Published).Delay);
    }

    [Fact]
    public async Task The_last_failed_attempt_exhausts_the_delivery()
    {
        _sender.Enqueue(Retry());

        var action = await Processor().ProcessAsync(Message(attempt: RetryPolicy.MaxAttempts), PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Exhausted, action);
        Assert.Empty(_retries.Published);
        Assert.Equal(DeliveryStatus.Exhausted, Assert.Single(_state.Rows).Status);
    }

    [Fact]
    public async Task A_permanent_failure_is_recorded_and_not_retried()
    {
        _sender.Enqueue(new SendResult(SendOutcome.Failed, 400, 20, "http_400"));

        var action = await Processor().ProcessAsync(Message(), PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Failed, action);
        Assert.Empty(_retries.Published);
        var row = Assert.Single(_state.Rows);
        Assert.Equal(DeliveryStatus.Failed, row.Status);
        Assert.Equal(400, row.LastStatusCode);
        Assert.Equal("http_400", row.LastError);
    }

    [Fact]
    public async Task A_delivery_that_waited_too_long_expires_without_being_sent()
    {
        var old = Message(notBefore: Now.AddHours(-25));

        var action = await Processor().ProcessAsync(old, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Expired, action);
        Assert.Equal(0, _sender.Calls);
        Assert.Equal(DeliveryStatus.Expired, Assert.Single(_state.Rows).Status);
    }

    [Fact]
    public async Task A_late_retry_is_not_expired_because_age_counts_from_when_it_was_due()
    {
        _sender.Enqueue(Ok());
        var lateRetry = Message(attempt: 9, notBefore: Now.AddSeconds(-5));

        var action = await Processor().ProcessAsync(lateRetry, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Delivered, action);
    }

    [Fact]
    public async Task A_missing_endpoint_fails_the_delivery_without_calling_anyone()
    {
        var message = Message() with { EndpointId = EndpointId.New() };

        var action = await Processor().ProcessAsync(message, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Failed, action);
        Assert.Equal(0, _sender.Calls);
        Assert.Equal("endpoint_unavailable", Assert.Single(_state.Rows).LastError);
    }

    [Fact]
    public async Task A_payload_that_cannot_be_found_fails_the_delivery()
    {
        var message = Message(payloadRef: "memory://archive/missing");

        var action = await Processor().ProcessAsync(message, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Failed, action);
        Assert.Equal("payload_unavailable", Assert.Single(_state.Rows).LastError);
    }

    [Fact]
    public async Task A_payload_by_reference_is_resolved_and_sent()
    {
        _payloads.Add("memory://archive/big", Payload);
        _sender.Enqueue(Ok());

        var action = await Processor().ProcessAsync(Message(payloadRef: "memory://archive/big"), PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Delivered, action);
        Assert.Equal(1, _sender.Calls);
    }

    [Theory]
    [InlineData(DeliveryStatus.Failed)]
    [InlineData(DeliveryStatus.Exhausted)]
    [InlineData(DeliveryStatus.Expired)]
    public async Task A_redelivered_message_for_a_finished_delivery_is_skipped(DeliveryStatus finalStatus)
    {
        var message = Message();
        await _state.PutAsync(Row(message, finalStatus), CancellationToken.None);

        var action = await Processor().ProcessAsync(message, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Skipped, action);
        Assert.Equal(0, _sender.Calls);
    }

    [Fact]
    public async Task A_redelivered_message_for_a_delivery_still_in_retry_is_processed()
    {
        var message = Message();
        await _state.PutAsync(Row(message, DeliveryStatus.RetryScheduled), CancellationToken.None);
        _sender.Enqueue(Ok());

        var action = await Processor().ProcessAsync(message, PlanTier.Basic, CancellationToken.None);

        Assert.Equal(DeliveryAction.Delivered, action);
    }

    [Fact]
    public async Task A_queue_failure_propagates_so_the_message_is_redelivered()
    {
        _sender.Enqueue(Retry());
        _retries.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Processor().ProcessAsync(Message(), PlanTier.Basic, CancellationToken.None));
    }

    private static DeliveryStateRecord Row(DeliveryMessage m, DeliveryStatus status) =>
        new(m.TenantId, m.DeliveryId, m.EndpointId, m.EventId, m.EventType, m.OccurredAt, status, m.Attempt, null, null, null, m.Payload, null, m.CreatedAt, m.NotBefore);

    private sealed class ScriptedSender : IWebhookSender
    {
        private readonly Queue<SendResult> _results = new();

        public int Calls { get; private set; }

        public void Enqueue(SendResult result) => _results.Enqueue(result);

        public ValueTask<SendResult> SendAsync(EndpointTarget target, DeliveryMessage message, JsonElement payload, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingRetryPublisher : IRetryPublisher
    {
        private readonly List<(PlanTier Tier, DeliveryMessage Message, TimeSpan Delay)> _published = [];

        public bool Fail { get; set; }

        public IReadOnlyList<(PlanTier Tier, DeliveryMessage Message, TimeSpan Delay)> Published => _published;

        public ValueTask PublishAsync(PlanTier tier, DeliveryMessage nextAttempt, TimeSpan delay, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("queue unavailable");
            }

            _published.Add((tier, nextAttempt, delay));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DictionaryPayloadResolver : IPayloadResolver
    {
        private readonly Dictionary<string, JsonElement> _byRef = [];

        public void Add(string reference, JsonElement payload) => _byRef[reference] = payload;

        public ValueTask<JsonElement?> ResolveAsync(DeliveryMessage message, CancellationToken cancellationToken) =>
            ValueTask.FromResult(message.Payload ?? (message.PayloadRef is { } r && _byRef.TryGetValue(r, out var p) ? p : (JsonElement?)null));
    }
}
