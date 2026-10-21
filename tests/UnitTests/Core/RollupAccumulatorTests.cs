using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Core.Rollups;

namespace WebhookGateway.UnitTests.Core;

public sealed class RollupAccumulatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 10, 0, 20, TimeSpan.Zero);
    private readonly TenantId _tenant = TenantId.New();
    private readonly EndpointId _a = EndpointId.New();
    private readonly EndpointId _b = EndpointId.New();

    private AttemptSample Sample(EndpointId endpoint, DeliveryOutcome outcome, long response = 100, long queueWait = 10, bool timedOut = false, DateTimeOffset? at = null) =>
        new(_tenant, endpoint, outcome, queueWait, response, timedOut, at ?? T0);

    [Fact]
    public void Counts_outcomes_per_endpoint_and_minute()
    {
        var accumulator = new RollupAccumulator();
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded));
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded));
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Retry, timedOut: true));
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Failed));
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Exhausted));

        var rows = accumulator.Drain();
        var row = rows[new RollupKey(_tenant, _a.Value, new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero))];

        Assert.Equal(5, row.Attempts);
        Assert.Equal(2, row.Succeeded);
        Assert.Equal(1, row.Retried);
        Assert.Equal(1, row.Failed);
        Assert.Equal(1, row.Exhausted);
        Assert.Equal(1, row.Timeouts);
    }

    [Fact]
    public void Samples_in_different_minutes_go_to_different_rows()
    {
        var accumulator = new RollupAccumulator();
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded, at: T0));
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded, at: T0.AddMinutes(1)));

        var rows = accumulator.Drain();

        Assert.Equal(2, rows.Keys.Count(k => k.EndpointKey == _a.Value));
    }

    [Fact]
    public void Drain_adds_a_tenant_wide_row_across_endpoints()
    {
        var accumulator = new RollupAccumulator();
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded, response: 100));
        accumulator.RecordAttempt(Sample(_b, DeliveryOutcome.Failed, response: 300));
        accumulator.RecordCreated(_tenant, _a, T0);

        var rows = accumulator.Drain();
        var all = rows.Single(r => r.Key.EndpointKey == RollupKey.AllEndpoints).Value;

        Assert.Equal(2, all.Attempts);
        Assert.Equal(1, all.Succeeded);
        Assert.Equal(1, all.Failed);
        Assert.Equal(1, all.Created);
        Assert.Equal(400, all.ConnectionMs);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Drain_empties_the_accumulator()
    {
        var accumulator = new RollupAccumulator();
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded));

        accumulator.Drain();

        Assert.Empty(accumulator.Drain());
    }

    [Fact]
    public void Latency_and_queue_wait_are_tracked_separately()
    {
        var accumulator = new RollupAccumulator();
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded, response: 900, queueWait: 3));

        var row = accumulator.Drain().Single(r => r.Key.EndpointKey == _a.Value).Value;

        Assert.Equal(1000, row.ResponseTime.Percentile(0.95));
        Assert.Equal(5, row.QueueWait.Percentile(0.95));
    }

    [Fact]
    public async Task Concurrent_recording_loses_nothing()
    {
        var accumulator = new RollupAccumulator();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded));
            }
        })));

        var all = accumulator.Drain().Single(r => r.Key.EndpointKey == RollupKey.AllEndpoints).Value;
        Assert.Equal(8000, all.Attempts);
        Assert.Equal(8000, all.Succeeded);
    }

    [Fact]
    public void Restored_rows_come_out_again_exactly_once_without_double_counting_the_tenant_row()
    {
        var accumulator = new RollupAccumulator();
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded));
        var drained = accumulator.Drain();

        accumulator.Restore(drained);
        accumulator.RecordAttempt(Sample(_a, DeliveryOutcome.Succeeded));
        var next = accumulator.Drain();

        Assert.Equal(2, next.Single(r => r.Key.EndpointKey == _a.Value).Value.Attempts);
        Assert.Equal(2, next.Single(r => r.Key.EndpointKey == RollupKey.AllEndpoints).Value.Attempts);
        Assert.Empty(accumulator.Drain());
    }
}
