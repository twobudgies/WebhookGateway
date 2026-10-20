namespace WebhookGateway.Ingest.Api;

/// <summary>Error body shared by all Ingest responses: <c>{ "error": { "code", "message" } }</c> (docs/contracts.md section 1).</summary>
public static class ApiErrors
{
    public static IResult Create(int statusCode, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: statusCode);
}
