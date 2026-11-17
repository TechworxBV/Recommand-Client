using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Recommand.Client;

/// <summary>
/// Hand-written conveniences on top of the generated <see cref="WebhookPayload"/>
/// polymorphism hierarchy. Adds parse helpers, a forward-compatible
/// <see cref="EventType"/> accessor. An event type this SDK version does
/// not recognise parses to <see cref="UnknownWebhookPayload"/> rather than
/// throwing.
/// </summary>
public partial class WebhookPayload
{
    /// <summary>
    /// The event-type identifier as it appears on the wire (e.g.
    /// <c>"document.received"</c>).
    /// </summary>
    /// <remarks>
    /// For events the SDK recognises, prefer pattern matching on the runtime
    /// type (<c>if (payload is DocumentReceivedWebhook d) …</c>); use
    /// <c>EventType</c> for logging, metrics, and for telling apart the
    /// events that arrive as <see cref="UnknownWebhookPayload"/>.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EventType => JsonInheritanceConverter<WebhookPayload>.GetDiscriminator(this);

    /// <summary>
    /// Parse a webhook delivery body to its concrete subtype.
    /// </summary>
    /// <remarks>
    /// When the wire <c>eventType</c> is missing or is not one this SDK
    /// version knows, the result is an <see cref="UnknownWebhookPayload"/>
    /// with <c>TeamId</c>/<c>CompanyId</c> set and every other field kept in
    /// <c>AdditionalProperties</c>, so consumers can log and ignore it.
    /// Returns <c>null</c> for null/empty input or a JSON <c>null</c>.
    /// </remarks>
    public static WebhookPayload? Parse(string? json, JsonSerializerOptions? options = null) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<WebhookPayload>(json!, options);

    /// <summary>
    /// Parse a webhook delivery body from a stream. Useful for raw HTTP
    /// handlers that read the request body directly.
    /// </summary>
    public static ValueTask<WebhookPayload?> ParseAsync(
        Stream stream,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        return JsonSerializer.DeserializeAsync<WebhookPayload>(stream, options, cancellationToken);
    }
}
