using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebhookGateway.Contracts;
using WebhookGateway.Contracts.Ids;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Hosting;

namespace WebhookGateway.UnitTests.Hosting;

public sealed class RollupFlusherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly RollupAccumulator _accumulator = new();
    private readonly ScriptedWriter _writer = new();

    private RollupFlusher Flusher() =>
        new(_accumulator, _writer, Options.Create(new RollupFlusherOptions()), NullLogger<RollupFlusher>.Instance);

    private void Record() =>
        _accumulator.RecordAttempt(new AttemptSample(TenantId.New(), EndpointId.New(), DeliveryOutcome.Succeeded, 5, 20, false, Now));

    [Fact]
    public async Task A_flush_hands_the_counters_to_the_writer_once()
    {
        Record();

        Assert.Equal(0, await Flusher().FlushAsync(CancellationToken.None));
        Assert.Equal(0, await Flusher().FlushAsync(CancellationToken.None));

        Assert.Single(_writer.Writes);
        Assert.Equal(2, _writer.Writes[0].Count);
    }

    [Fact]
    public async Task A_writer_exception_keeps_the_counters_for_the_next_flush()
    {
        Record();
        _writer.Throw = true;
        var flusher = Flusher();

        Assert.Equal(2, await flusher.FlushAsync(CancellationToken.None));
        _writer.Throw = false;
        Assert.Equal(0, await flusher.FlushAsync(CancellationToken.None));

        Assert.Equal(2, Assert.Single(_writer.Writes).Count);
    }

    [Fact]
    public async Task Rows_the_writer_could_not_store_are_kept_for_the_next_flush()
    {
        Record();
        _writer.FailAll = true;
        var flusher = Flusher();

        Assert.Equal(2, await flusher.FlushAsync(CancellationToken.None));
        _writer.FailAll = false;
        Assert.Equal(0, await flusher.FlushAsync(CancellationToken.None));

        Assert.Equal(2, _writer.Writes[^1].Count);
    }

    private sealed class ScriptedWriter : IRollupWriter
    {
        public List<IReadOnlyDictionary<RollupKey, RollupRow>> Writes { get; } = [];

        public bool Throw { get; set; }

        public bool FailAll { get; set; }

        public ValueTask<IReadOnlyDictionary<RollupKey, RollupRow>> WriteAsync(
            IReadOnlyDictionary<RollupKey, RollupRow> rows,
            CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("dynamodb down");
            }

            Writes.Add(rows);
            return ValueTask.FromResult<IReadOnlyDictionary<RollupKey, RollupRow>>(FailAll ? rows : new Dictionary<RollupKey, RollupRow>());
        }
    }
}
