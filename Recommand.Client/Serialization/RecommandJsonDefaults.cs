using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recommand.Client;

/// <summary>
/// Default <see cref="JsonSerializerOptions"/> configuration shared by every
/// generated resource client (<see cref="SendingClient"/>,
/// <see cref="DocumentsClient"/>, etc.). Each sub-client's
/// <c>UpdateJsonSerializerSettings</c> partial method calls into here.
/// </summary>
internal static class RecommandJsonDefaults
{
    /// <summary>
    /// Apply the SDK-wide serialization policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Omit null-valued nullable properties.</b> NSwag's generated DTOs
    /// declare every optional property as nullable; STJ's default is to emit
    /// each one as <c>"foo": null</c> regardless of whether the caller set
    /// it. For an SDK talking to OpenAPI endpoints this is wrong — the
    /// server frequently distinguishes between <i>present-and-null</i>
    /// ("clear this field") and <i>absent</i> ("leave default / no change"),
    /// and the noise also bloats payloads and logs.
    /// </para>
    /// <para>
    /// Callers must explicitly set any property they want sent as
    /// <c>null</c>. The one property in the current API surface where this
    /// matters in practice is <see cref="SendDocumentRequest.Recipient"/>
    /// (typed nullable + required in the spec; <c>null</c> means "email-only
    /// delivery"). Callers must always assign a value to that property —
    /// either a Peppol address or an explicit <c>null</c>; leaving it at the
    /// default <c>null</c> works in C# but the server expects the JSON
    /// field to be present.
    /// </para>
    /// </remarks>
    public static void ConfigureCommon(JsonSerializerOptions settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}
