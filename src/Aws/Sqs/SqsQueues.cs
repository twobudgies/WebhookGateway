using Amazon.SQS;
using WebhookGateway.Contracts;

namespace WebhookGateway.Aws.Sqs;

/// <summary>Queue names from docs/data-plane.md.</summary>
public sealed class SqsQueueNames
{
    public string Events { get; init; } = "events";

    public static string Deliveries(PlanTier tier) => $"deliveries-{tier.ToString().ToLowerInvariant()}";
}

/// <summary>Resolves queue names to URLs once and caches them.</summary>
public sealed class SqsQueues(IAmazonSQS sqs, SqsQueueNames names)
{
    private readonly Dictionary<string, Task<string>> _urls = [];

    public SqsQueueNames Names => names;

    public IAmazonSQS Client => sqs;

    public Task<string> UrlAsync(string queueName, CancellationToken cancellationToken)
    {
        lock (_urls)
        {
            if (!_urls.TryGetValue(queueName, out var url) || url.IsFaulted || url.IsCanceled)
            {
                _urls[queueName] = url = ResolveAsync(queueName, cancellationToken);
            }

            return url;
        }
    }

    private async Task<string> ResolveAsync(string queueName, CancellationToken cancellationToken) =>
        (await sqs.GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false)).QueueUrl;
}
