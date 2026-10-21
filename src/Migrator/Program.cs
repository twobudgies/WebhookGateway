using Microsoft.Extensions.Logging;
using Npgsql;
using WebhookGateway.Hosting;
using WebhookGateway.Postgres;

// Usage: Migrator <connection string> [migrations directory]   (or MIGRATIONS_CONNECTION)
var connectionString = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("MIGRATIONS_CONNECTION");
var directory = args.Length > 1 ? args[1] : "db/migrations";
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("Usage: Migrator <connection string> [migrations directory]");
    return 2;
}

using var loggerFactory = LoggerFactory.Create(b => b.AddStructuredJsonConsole("Migrator"));
await using var dataSource = NpgsqlDataSource.Create(connectionString);
var runner = new MigrationRunner(dataSource, loggerFactory.CreateLogger<MigrationRunner>());
await runner.RunAsync(directory, CancellationToken.None);
return 0;
