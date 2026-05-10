namespace Recommand.Client.AspNetCore;

/// <summary>
/// Configuration for an endpoint registered via
/// <c>MapRecommandWebhook</c>. Either pass an instance to
/// <c>MapRecommandWebhook</c> directly, or register
/// <c>IOptions&lt;RecommandWebhookOptions&gt;</c> via
/// <c>services.AddRecommandWebhooks(...)</c> for it to be picked up
/// from DI by the endpoint.
/// </summary>
public sealed class RecommandWebhookOptions
{
    /// <summary>
    /// Shared HMAC-SHA256 secret used to verify the <c>X-Signature</c>
    /// header. When <c>null</c>, signatures are <b>not</b> verified — only
    /// acceptable for local development or when verification happens out of
    /// band. Production endpoints exposed on the public internet should
    /// always have a secret configured.
    /// </summary>
    public string? SigningSecret { get; set; }

    /// <summary>
    /// When <c>true</c> (the default) and <see cref="SigningSecret"/> is set,
    /// deliveries arriving without a valid <c>X-Signature</c> header are
    /// rejected with <c>401 Unauthorized</c>. When <c>false</c>, missing
    /// signatures pass through (still rejected if a signature is present and
    /// invalid). Useful when initially rolling out signing and not all
    /// deliveries are signed yet — flip back to <c>true</c> once you've
    /// confirmed every delivery is signed.
    /// </summary>
    public bool RequireSignatureWhenSecretConfigured { get; set; } = true;

    /// <summary>
    /// Maximum allowed request body size in bytes. Deliveries exceeding this
    /// are rejected with <c>413 Payload Too Large</c> before parsing.
    /// Default 1 MiB — the spec doesn't document a hard cap, but 1 MiB is
    /// generous for any payload we've seen.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 1 * 1024 * 1024;
}
