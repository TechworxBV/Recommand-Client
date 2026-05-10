using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Recommand.Client;

/// <summary>
/// Hand-written conveniences on top of the generated <see cref="WebhookPayload"/>
/// polymorphism hierarchy. Adds parse helpers, a forward-compatible
/// <see cref="EventType"/> accessor, and <i>safe</i> deserialisation that
/// doesn't throw when the API sends an event type this SDK version doesn't
/// recognise.
/// </summary>
public partial class WebhookPayload
{
    /// <summary>
    /// The event-type identifier as it appears on the wire (e.g.
    /// <c>"document.received"</c>).
    /// </summary>
    /// <remarks>
    /// NSwag's <c>JsonInheritanceConverter</c> dispatches deserialisation by
    /// reading the discriminator field directly from JSON, so the parent class
    /// has no first-class C# property for it. This getter pulls the value
    /// from <see cref="AdditionalProperties"/>. For events the SDK
    /// recognises, prefer pattern matching on the runtime type
    /// (<c>if (payload is DocumentReceivedWebhook d) …</c>); use
    /// <c>EventType</c> for logging, metrics, and handling unknown future
    /// events that arrive as the base <see cref="WebhookPayload"/>.
    /// </remarks>
    public string? EventType =>
        AdditionalProperties is not null
        && AdditionalProperties.TryGetValue("eventType", out var value)
            ? value?.ToString()
            : null;

    /// <summary>
    /// Parse a webhook delivery body to its concrete subtype.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forward-compat behaviour: when the wire <c>eventType</c> does not match
    /// any subclass this SDK version knows about, returns a base
    /// <see cref="WebhookPayload"/> with all fields preserved in
    /// <see cref="AdditionalProperties"/> (including the unknown
    /// <c>eventType</c>, accessible via <see cref="EventType"/>). This avoids
    /// the <c>InvalidOperationException</c> that NSwag's
    /// <c>JsonInheritanceConverter</c> would otherwise throw on unknown
    /// discriminators, so consumers can safely log-and-ignore unrecognised
    /// events without crashing.
    /// </para>
    /// <para>
    /// Returns <c>null</c> only for null/empty input or when the JSON root is
    /// not an object.
    /// </para>
    /// </remarks>
    public static WebhookPayload? Parse(string? json, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using var doc = JsonDocument.Parse(json);
        return ParseElement(doc.RootElement, json, options);
    }

    /// <summary>
    /// Parse a webhook delivery body from a stream. Useful for raw HTTP
    /// handlers that read the request body directly.
    /// </summary>
    public static async ValueTask<WebhookPayload?> ParseAsync(
        Stream stream,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        // ParseElement re-serialises if needed, so it's stream-safe.
        return ParseElement(doc.RootElement, rawJson: null, options);
    }

    private static WebhookPayload? ParseElement(
        JsonElement root,
        string? rawJson,
        JsonSerializerOptions? options)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        var eventType = root.TryGetProperty("eventType", out var et)
                        && et.ValueKind == JsonValueKind.String
            ? et.GetString()
            : null;

        if (eventType is not null && KnownDiscriminators.Value.Contains(eventType))
        {
            // Route through the JsonInheritanceConverter for typed dispatch.
            // We materialise the JSON if we only had a JsonElement (stream path)
            // to avoid double-parsing in the string path.
            var json = rawJson ?? root.GetRawText();
            return JsonSerializer.Deserialize<WebhookPayload>(json, options);
        }

        // Unknown / missing eventType: deserialise into a bare WebhookPayload,
        // preserving all wire fields in AdditionalProperties. The polymorphism
        // converter would otherwise throw with "Could not find subtype …".
        return BuildBasePayload(root);
    }

    private static WebhookPayload BuildBasePayload(JsonElement root)
    {
        var payload = new WebhookPayload
        {
            TeamId = TryGetString(root, "teamId") ?? string.Empty,
            CompanyId = TryGetString(root, "companyId") ?? string.Empty,
        };

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name is "teamId" or "companyId") continue;
            // AdditionalProperties is IDictionary<string, object> (non-nullable
            // value type per generated signature); JsonExtensionData accepts
            // null at runtime regardless. Suppress the analyzer.
            payload.AdditionalProperties[prop.Name] = ToObject(prop.Value)!;
        }
        return payload;
    }

    private static string? TryGetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static object? ToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? (object)i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.Clone(),  // object or array — preserve as JsonElement for inspection
    };

    // Set of discriminator values this SDK version has typed subclasses for,
    // discovered once via reflection on the JsonInheritanceAttributes the
    // generator emits. Stays in sync automatically as the spec/generator add
    // or remove variants.
    private static readonly Lazy<HashSet<string>> KnownDiscriminators = new(() =>
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in typeof(WebhookPayload).GetCustomAttributes(inherit: true))
        {
            // JsonInheritanceAttribute is internal — same assembly, so we can
            // see it but can't name the type from here. Reflect by name.
            var type = attr.GetType();
            if (type.Name != "JsonInheritanceAttribute") continue;
            var keyProp = type.GetProperty("Key");
            if (keyProp?.GetValue(attr) is string key) set.Add(key);
        }
        return set;
    });
}
