using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Recommand.Client.Authentication;

/// <summary>
/// HTTP message handler that injects an <c>Authorization: Basic</c> header on
/// every outgoing request, sourcing credentials from
/// <see cref="RecommandClientOptions"/>. Credentials are read on every request
/// via <see cref="IOptionsMonitor{TOptions}"/> so configuration reloads take
/// effect without re-creating the handler chain.
/// </summary>
internal sealed class BasicAuthenticationHandler : DelegatingHandler
{
    private readonly Func<(string? ApiKey, string? ApiSecret)> _credentials;

    /// <summary>DI ctor — reads credentials from configured options on each request.</summary>
    public BasicAuthenticationHandler(IOptionsMonitor<RecommandClientOptions> options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        _credentials = () =>
        {
            var current = options.CurrentValue;
            return (current.ApiKey, current.ApiSecret);
        };
    }

    /// <summary>Non-DI ctor — credentials are captured at construction time.</summary>
    public BasicAuthenticationHandler(string apiKey, string apiSecret)
    {
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("API key is required.", nameof(apiKey));
        if (string.IsNullOrEmpty(apiSecret)) throw new ArgumentException("API secret is required.", nameof(apiSecret));
        _credentials = () => (apiKey, apiSecret);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var (apiKey, apiSecret) = _credentials();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                $"{nameof(RecommandClientOptions)}.{nameof(RecommandClientOptions.ApiKey)} is not configured.");
        if (string.IsNullOrEmpty(apiSecret))
            throw new InvalidOperationException(
                $"{nameof(RecommandClientOptions)}.{nameof(RecommandClientOptions.ApiSecret)} is not configured.");

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        return base.SendAsync(request, cancellationToken);
    }
}
