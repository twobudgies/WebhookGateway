using Amazon;
using Amazon.DynamoDBv2;
using Amazon.KeyManagementService;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WebhookGateway.Aws.DynamoDb;
using WebhookGateway.Aws.Kms;
using WebhookGateway.Aws.S3;
using WebhookGateway.Aws.Sqs;
using WebhookGateway.Core.Rollups;
using WebhookGateway.Dispatcher.Delivery;
using WebhookGateway.Dispatcher.Endpoints;
using WebhookGateway.Dispatcher.Source;
using WebhookGateway.FanOut.Archive;
using WebhookGateway.FanOut.Delivery;
using WebhookGateway.FanOut.Source;
using WebhookGateway.Ingest.Publishing;

namespace WebhookGateway.Aws;

/// <summary>
/// Switches a service from its in-memory ports to the AWS adapters with <c>Backend:Mode=Aws</c>. Each role registers
/// only what it needs, so a service never creates clients it does not use (docs/flow-walkthrough.md).
/// </summary>
public static class AwsServiceCollectionExtensions
{
    public static bool UseAws(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration[AwsOptions.ModeKey], "Aws", StringComparison.OrdinalIgnoreCase);
    }

    public static IServiceCollection AddAwsIngest(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAwsQueues(configuration);
        services.Replace(ServiceDescriptor.Singleton<IEventPublisher, SqsEventPublisher>());
        return services;
    }

    public static IServiceCollection AddAwsFanOut(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAwsQueues(configuration);
        services.AddAwsS3(configuration);
        services.Replace(ServiceDescriptor.Singleton<IEventSource, SqsEventSource>());
        services.Replace(ServiceDescriptor.Singleton<IDeliveryPublisher, SqsDeliveryPublisher>());
        services.Replace(ServiceDescriptor.Singleton<IEventArchive, S3EventArchive>());
        return services;
    }

    public static IServiceCollection AddAwsDispatcher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAwsQueues(configuration);
        services.AddAwsS3(configuration);
        services.AddAwsDynamoDb(configuration);
        services.Replace(ServiceDescriptor.Singleton<IDeliverySource, SqsDeliverySource>());
        services.Replace(ServiceDescriptor.Singleton<IRetryPublisher, SqsDeliveryPublisher>());
        services.Replace(ServiceDescriptor.Singleton<IPayloadResolver, S3PayloadResolver>());
        services.TryAddSingleton<DynamoDbDeliveryStateStore>();
        services.Replace(ServiceDescriptor.Singleton<IDeliveryStateStore>(sp => sp.GetRequiredService<DynamoDbDeliveryStateStore>()));
        services.Replace(ServiceDescriptor.Singleton<IDueDeliveryIndex>(sp => sp.GetRequiredService<DynamoDbDeliveryStateStore>()));
        return services;
    }

    /// <summary>The DynamoDB rollup writer; used by every service that counts (Fan-out and Dispatcher).</summary>
    public static IServiceCollection AddAwsRollups(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAwsDynamoDb(configuration);
        services.TryAddSingleton<DynamoDbRollupWriter>();
        services.Replace(ServiceDescriptor.Singleton<IRollupWriter>(sp => sp.GetRequiredService<DynamoDbRollupWriter>()));
        return services;
    }

    /// <summary>
    /// Endpoint-secret encryption (D26): the KMS protector in production (<c>Secrets:KmsKeyId</c>), or the local AES key
    /// (<c>Secrets:LocalKey</c>, base64 32 bytes) for development. Neither configured is a startup error.
    /// </summary>
    public static IServiceCollection AddSecretProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var localKey = configuration["Secrets:LocalKey"];
        if (!string.IsNullOrEmpty(localKey))
        {
            services.AddSingleton<ISecretProtector>(LocalSecretProtector.FromBase64(localKey));
            return services;
        }

        var kmsKeyId = configuration["Secrets:KmsKeyId"];
        if (string.IsNullOrEmpty(kmsKeyId))
        {
            throw new InvalidOperationException("Configure Secrets:KmsKeyId (production) or Secrets:LocalKey (development) to protect endpoint secrets.");
        }

        services.TryAddSingleton<IAmazonKeyManagementService>(_ => Configure<AmazonKeyManagementServiceConfig, IAmazonKeyManagementService>(
            configuration,
            (creds, c) => creds is null ? new AmazonKeyManagementServiceClient(c) : new AmazonKeyManagementServiceClient(creds, c)));
        services.AddSingleton<ISecretProtector>(sp => new KmsSecretProtector(
            sp.GetRequiredService<IAmazonKeyManagementService>(),
            kmsKeyId,
            sp.GetRequiredService<ILogger<KmsSecretProtector>>()));
        return services;
    }

    private static void AddAwsQueues(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IAmazonSQS>(_ => Configure<AmazonSQSConfig, IAmazonSQS>(configuration, (creds, c) => creds is null ? new AmazonSQSClient(c) : new AmazonSQSClient(creds, c)));
        services.TryAddSingleton(new SqsQueueNames());
        services.TryAddSingleton<SqsQueues>();
        services.TryAddSingleton<SqsDeliveryPublisher>();
    }

    private static void AddAwsS3(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IAmazonS3>(_ => Configure<AmazonS3Config, IAmazonS3>(configuration, (creds, c) =>
        {
            c.ForcePathStyle = c.ServiceURL is not null;
            return creds is null ? new AmazonS3Client(c) : new AmazonS3Client(creds, c);
        }));
        services.Configure<ArchiveOptions>(configuration.GetSection("Aws:Archive"));
    }

    private static void AddAwsDynamoDb(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IAmazonDynamoDB>(_ => Configure<AmazonDynamoDBConfig, IAmazonDynamoDB>(configuration, (creds, c) => creds is null ? new AmazonDynamoDBClient(c) : new AmazonDynamoDBClient(creds, c)));
        services.Configure<DynamoDbOptions>(configuration.GetSection("Aws:DynamoDb"));
    }

    private static TClient Configure<TConfig, TClient>(IConfiguration configuration, Func<AWSCredentials?, TConfig, TClient> create)
        where TConfig : ClientConfig, new()
    {
        var options = configuration.GetSection(AwsOptions.SectionName).Get<AwsOptions>() ?? new AwsOptions();
        var config = new TConfig();
        if (!string.IsNullOrEmpty(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return create(Credentials(options), config);
    }

    /// <summary>Emulator credentials, or null so the SDK uses the default chain (the task role in production).</summary>
    private static BasicAWSCredentials? Credentials(AwsOptions options) =>
        string.IsNullOrEmpty(options.ServiceUrl) ? null : new BasicAWSCredentials(options.AccessKey ?? "local", options.SecretKey ?? "local");
}
