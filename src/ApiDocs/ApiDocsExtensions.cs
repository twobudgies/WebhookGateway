using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace WebhookGateway.ApiDocs;

/// <summary>How callers authenticate; drives the "Authorize" button in the docs page.</summary>
public abstract record ApiSecurity
{
    /// <summary>The scheme is shown as <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
    public sealed record Bearer(string Description) : ApiSecurity;

    /// <summary>The scheme is a key in a request header, for example <c>X-Admin-Key</c>.</summary>
    public sealed record HeaderKey(string HeaderName, string Description) : ApiSecurity;
}

/// <summary>
/// Publishes the OpenAPI definition at <c>/openapi/v1.json</c> and an interactive Swagger UI at <c>/docs</c>. Anyone can
/// read the docs; "Try it out" needs a key, which is entered with the Authorize button. Turn it off with
/// <c>ApiDocs:Enabled=false</c>.
/// </summary>
public static class ApiDocsExtensions
{
    private const string SchemeName = "ApiKey";

    public static bool DocsEnabled(this IConfiguration configuration) =>
        !string.Equals(configuration?["ApiDocs:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddApiDocs(this IServiceCollection services, string title, string description, ApiSecurity security)
    {
        ArgumentNullException.ThrowIfNull(security);
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo { Title = title, Version = "v1", Description = description };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeName] = security switch
            {
                ApiSecurity.Bearer b => new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = b.Description },
                ApiSecurity.HeaderKey h => new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = h.HeaderName, Description = h.Description },
                _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unknown security scheme."),
            };

            foreach (var (path, item) in document.Paths)
            {
                if (path.StartsWith("/healthz", StringComparison.Ordinal) || item.Operations is null)
                {
                    continue;
                }

                foreach (var operation in item.Operations.Values)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, document)] = [] });
                }
            }

            return Task.CompletedTask;
        }));
        return services;
    }

    public static WebApplication UseApiDocs(this WebApplication app, string title)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "docs";
            options.DocumentTitle = title;
            options.SwaggerEndpoint("/openapi/v1.json", title);
            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
        });
        return app;
    }
}
