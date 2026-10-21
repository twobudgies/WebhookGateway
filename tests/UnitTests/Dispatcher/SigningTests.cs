using System.Text;
using WebhookGateway.Dispatcher.Signing;

namespace WebhookGateway.UnitTests.Dispatcher;

public sealed class SigningTests
{
    // Test vector from the Standard Webhooks specification.
    private const string VectorSecret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string VectorId = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const long VectorTimestamp = 1614265330;
    private const string VectorBody = """{"test": 2432232314}""";
    private const string VectorSignature = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";

    [Fact]
    public void Matches_the_standard_webhooks_test_vector()
    {
        var signature = WebhookSigner.Sign(VectorId, VectorTimestamp, Encoding.UTF8.GetBytes(VectorBody), [SigningSecret.Parse(VectorSecret)]);

        Assert.Equal(VectorSignature, signature);
    }

    [Fact]
    public void Two_secrets_produce_two_space_separated_signatures_for_rotation()
    {
        var old = SigningSecret.Parse(VectorSecret);
        var fresh = SigningSecret.Generate();

        var signature = WebhookSigner.Sign(VectorId, VectorTimestamp, Encoding.UTF8.GetBytes(VectorBody), [old, fresh]);

        var parts = signature.Split(' ');
        Assert.Equal(2, parts.Length);
        Assert.Equal(VectorSignature, parts[0]);
        Assert.NotEqual(parts[0], parts[1]);
    }

    [Fact]
    public void Signature_changes_with_the_body_id_and_timestamp()
    {
        var secrets = new[] { SigningSecret.Parse(VectorSecret) };
        var body = Encoding.UTF8.GetBytes(VectorBody);

        var baseline = WebhookSigner.Sign(VectorId, VectorTimestamp, body, secrets);

        Assert.NotEqual(baseline, WebhookSigner.Sign(VectorId, VectorTimestamp, Encoding.UTF8.GetBytes("{}"), secrets));
        Assert.NotEqual(baseline, WebhookSigner.Sign("msg_other", VectorTimestamp, body, secrets));
        Assert.NotEqual(baseline, WebhookSigner.Sign(VectorId, VectorTimestamp + 1, body, secrets));
    }

    [Fact]
    public void Signing_without_a_secret_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => WebhookSigner.Sign(VectorId, VectorTimestamp, [], []));
    }

    [Fact]
    public void Generated_secret_roundtrips_through_its_text_form()
    {
        var secret = SigningSecret.Generate();

        var parsed = SigningSecret.Parse(secret.ToText());

        Assert.StartsWith("whsec_", secret.ToText(), StringComparison.Ordinal);
        Assert.Equal(secret.ToText(), parsed.ToText());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("whsec_")]
    [InlineData("whsec_!!!not base64!!!")]
    public void Invalid_secret_text_is_rejected(string text)
    {
        Assert.Throws<FormatException>(() => SigningSecret.Parse(text));
    }
}
