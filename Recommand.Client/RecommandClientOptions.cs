using System;

namespace Recommand.Client;

/// <summary>
/// Configuration for <see cref="RecommandClient"/>. API key and secret are
/// minted in the Recommand dashboard at https://app.recommand.eu/api-keys.
/// </summary>
public sealed class RecommandClientOptions
{
    /// <summary>
    /// API key from the Recommand dashboard. Used as the username in HTTP Basic auth.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// API secret paired with <see cref="ApiKey"/>. Used as the password in HTTP Basic auth.
    /// </summary>
    public string? ApiSecret { get; set; }

    /// <summary>
    /// Base URL of the Recommand API. Defaults to the production endpoint.
    /// Override only when targeting a non-production environment.
    /// </summary>
    public string BaseUrl { get; set; } = "https://app.recommand.eu";

    /// <summary>
    /// Optional HTTP request timeout. When null, the underlying
    /// <see cref="System.Net.Http.HttpClient"/> default is used.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
