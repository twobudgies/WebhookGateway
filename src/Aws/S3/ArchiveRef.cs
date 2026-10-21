using System.Globalization;

namespace WebhookGateway.Aws.S3;

/// <summary>Pointer to one event inside an archive object: <c>s3://bucket/key#line</c> (zero-based line).</summary>
internal readonly record struct ArchiveRef(string Bucket, string Key, int Line)
{
    public override string ToString() => $"s3://{Bucket}/{Key}#{Line.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? value, out ArchiveRef result)
    {
        result = default;
        const string scheme = "s3://";
        if (value is null || !value.StartsWith(scheme, StringComparison.Ordinal))
        {
            return false;
        }

        var hash = value.LastIndexOf('#');
        var slash = value.IndexOf('/', scheme.Length);
        if (hash < 0 || slash < 0 || slash > hash || !int.TryParse(value[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var line))
        {
            return false;
        }

        result = new ArchiveRef(value[scheme.Length..slash], value[(slash + 1)..hash], line);
        return true;
    }
}
