namespace WebhookGateway.Core.Rollups;

/// <summary>Stores drained rollups (DynamoDB <c>rollups</c> in production), adding to rows that already exist.</summary>
public interface IRollupWriter
{
    /// <returns>The rows that could not be written (empty when all were); the caller restores them for the next flush.</returns>
    ValueTask<IReadOnlyDictionary<RollupKey, RollupRow>> WriteAsync(IReadOnlyDictionary<RollupKey, RollupRow> rows, CancellationToken cancellationToken);
}
