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
public sealed class RecommandClient : IRecommandClient, IDisposable
{
    private readonly AuthenticationClient _authentication;
    private readonly CompaniesClient _companies;
    private readonly CompanyDocumentTypesClient _companyDocumentTypes;
    private readonly CompanyIdentifiersClient _companyIdentifiers;
    private readonly CompanyNotificationEmailAddressesClient _companyNotificationEmailAddresses;
    private readonly CustomersClient _customers;
    private readonly DocumentsClient _documents;
    private readonly LabelsClient _labels;
    private readonly PlaygroundsClient _playgrounds;
    private readonly RecipientsClient _recipients;
    private readonly SendingClient _sending;
    private readonly SuppliersClient _suppliers;
    private readonly WebhooksClient _webhooks;

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

        _authentication                    = new AuthenticationClient(httpClient);
        _companies                         = new CompaniesClient(httpClient);
        _companyDocumentTypes              = new CompanyDocumentTypesClient(httpClient);
        _companyIdentifiers                = new CompanyIdentifiersClient(httpClient);
        _companyNotificationEmailAddresses = new CompanyNotificationEmailAddressesClient(httpClient);
        _customers                         = new CustomersClient(httpClient);
        _documents                         = new DocumentsClient(httpClient);
        _labels                            = new LabelsClient(httpClient);
        _playgrounds                       = new PlaygroundsClient(httpClient);
        _recipients                        = new RecipientsClient(httpClient);
        _sending                           = new SendingClient(httpClient);
        _suppliers                         = new SuppliersClient(httpClient);
        _webhooks                          = new WebhooksClient(httpClient);

        if (httpClient.BaseAddress is { } baseAddress)
        {
            var baseUrl = baseAddress.ToString();
            _authentication.BaseUrl                    = baseUrl;
            _companies.BaseUrl                         = baseUrl;
            _companyDocumentTypes.BaseUrl              = baseUrl;
            _companyIdentifiers.BaseUrl                = baseUrl;
            _companyNotificationEmailAddresses.BaseUrl = baseUrl;
            _customers.BaseUrl                         = baseUrl;
            _documents.BaseUrl                         = baseUrl;
            _labels.BaseUrl                            = baseUrl;
            _playgrounds.BaseUrl                       = baseUrl;
            _recipients.BaseUrl                        = baseUrl;
            _sending.BaseUrl                           = baseUrl;
            _suppliers.BaseUrl                         = baseUrl;
            _webhooks.BaseUrl                          = baseUrl;
        }
    }

    /// <inheritdoc />
    public IAuthenticationClient Authentication                                       => _authentication;
    /// <inheritdoc />
    public ICompaniesClient Companies                                                 => _companies;
    /// <inheritdoc />
    public ICompanyDocumentTypesClient CompanyDocumentTypes                           => _companyDocumentTypes;
    /// <inheritdoc />
    public ICompanyIdentifiersClient CompanyIdentifiers                               => _companyIdentifiers;
    /// <inheritdoc />
    public ICompanyNotificationEmailAddressesClient CompanyNotificationEmailAddresses => _companyNotificationEmailAddresses;
    /// <inheritdoc />
    public ICustomersClient Customers                                                 => _customers;
    /// <inheritdoc />
    public IDocumentsClient Documents                                                 => _documents;
    /// <inheritdoc />
    public ILabelsClient Labels                                                       => _labels;
    /// <inheritdoc />
    public IPlaygroundsClient Playgrounds                                             => _playgrounds;
    /// <inheritdoc />
    public IRecipientsClient Recipients                                               => _recipients;
    /// <inheritdoc />
    public ISendingClient Sending                                                     => _sending;
    /// <inheritdoc />
    public ISuppliersClient Suppliers                                                 => _suppliers;
    /// <inheritdoc />
    public IWebhooksClient Webhooks                                                   => _webhooks;

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
        var client = new HttpClient(auth);
        if (!string.IsNullOrEmpty(baseUrl))
        {
            client.BaseAddress = new Uri(baseUrl);
        }
        return client;
    }
}
