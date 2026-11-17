using System;
using System.Net.Http;
using Recommand.Client.Authentication;

namespace Recommand.Client;

/// <summary>
/// Default <see cref="IRecommandClient"/> implementation. Holds a single
/// <see cref="HttpClient"/> shared across all typed resource clients.
/// </summary>
/// <remarks>
/// For dependency-injected applications, register via
/// <c>services.AddRecommandClient(o =&gt; { ... })</c> and inject
/// <see cref="IRecommandClient"/>.
/// For console apps and scripts, construct directly with
/// <see cref="RecommandClient(string, string, string)"/>.
/// Instances are thread-safe and intended to be long-lived (singleton).
/// </remarks>
public sealed partial class RecommandClient : IRecommandClient, IDisposable
{
    private readonly HttpClient? _ownedHttpClient;

    /// <summary>
    /// Creates a client using HTTP Basic auth with the given API key and
    /// secret. Constructs an <see cref="HttpClient"/> internally; that client
    /// is disposed when this instance is disposed.
    /// </summary>
    /// <param name="apiKey">API key from the Recommand dashboard.</param>
    /// <param name="apiSecret">API secret paired with <paramref name="apiKey"/>.</param>
    /// <param name="baseUrl">
    /// Optional override for the API base URL. Defaults to <c>https://app.recommand.eu</c>.
    /// </param>
    public RecommandClient(string apiKey, string apiSecret, string? baseUrl = null)
        : this(BuildOwnedHttpClient(apiKey, apiSecret, baseUrl), ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Creates a client wrapping an existing <see cref="HttpClient"/>. The
    /// caller is responsible for the HttpClient's lifetime, including auth.
    /// Requests go to its <see cref="HttpClient.BaseAddress"/>; when that is
    /// not set, it is set to the production API.
    /// Used by the DI registration; consumers can use this directly when
    /// integrating with <see cref="System.Net.Http.IHttpClientFactory"/>.
    /// </summary>
    public RecommandClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private RecommandClient(HttpClient httpClient, bool ownsHttpClient)
    {
        if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));
        _ownedHttpClient = ownsHttpClient ? httpClient : null;

        // Generated requests are relative; without a base address they would
        // have nowhere to go.
        httpClient.BaseAddress ??= new Uri(RecommandClientOptions.DefaultBaseUrl);
        CreateResourceClients(httpClient);
    }

    /// <summary>
    /// Disposes the internally-owned <see cref="HttpClient"/> if one was created.
    /// When the instance was constructed with an externally-provided HttpClient,
    /// the caller retains responsibility for its lifetime and Dispose is a no-op.
    /// </summary>
    public void Dispose() => _ownedHttpClient?.Dispose();

    private static HttpClient BuildOwnedHttpClient(string apiKey, string apiSecret, string? baseUrl)
    {
        var auth = new BasicAuthenticationHandler(apiKey, apiSecret)
        {
            InnerHandler = new HttpClientHandler()
        };
        return new HttpClient(auth)
        {
            BaseAddress = new Uri(string.IsNullOrEmpty(baseUrl) ? RecommandClientOptions.DefaultBaseUrl : baseUrl),
        };
    }
}
