# Recommand.Client

Unofficial .NET client for the [Recommand Peppol API](https://recommand.eu).
Generated from the [OpenAPI specification](https://peppol.recommand.eu/api-reference)
with [NSwag](https://github.com/RicoSuter/NSwag) and packaged with HTTP Basic
authentication, dependency-injection wiring, and `System.Text.Json`
serialisation.

> **Status:** Pre-1.0 — public surface may change between minor versions.
> Pin to an exact version until 1.0.

## Install

```sh
dotnet add package Recommand.Client
```

Targets `netstandard2.1`, so it runs on .NET Core 3.0+, .NET 5+, .NET Framework
via netstandard, and Mono/Unity environments that support netstandard2.1.

## Quick start (with dependency injection)

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddRecommandClient(o =>
{
    o.ApiKey    = builder.Configuration["Recommand:ApiKey"];
    o.ApiSecret = builder.Configuration["Recommand:ApiSecret"];
});
```

Then inject `IRecommandClient` anywhere — or, if a service only needs one
resource group, inject just that sub-client. Each sub-client interface is
registered separately, so you can mock exactly the surface you depend on:

```csharp
// Option 1: inject the root client.
public sealed class InvoiceSender(IRecommandClient recommand)
{
    public Task SendAsync(string companyId, SendDocumentRequest invoice, CancellationToken ct = default)
        => recommand.Sending.SendDocumentAsync(companyId, invoice, ct);
}

// Option 2: inject only what you use — easier to mock in tests.
public sealed class CompanyService(ICompaniesClient companies)
{
    public Task<GetCompanyResponse> GetAsync(string id, CancellationToken ct = default)
        => companies.GetCompanyAsync(id, ct);
}
```

Within a single DI scope (one HTTP request in ASP.NET Core), every injected
sub-client is backed by the same root `RecommandClient`, so the underlying
`HttpClient` is shared.

The returned `IHttpClientBuilder` lets you chain resilience, logging, or any
other `DelegatingHandler`:

```csharp
services.AddRecommandClient(o => { ... })
    .AddStandardResilienceHandler();
```

## Quick start (without dependency injection)

```csharp
using Recommand.Client;

using var recommand = new RecommandClient(apiKey: "key_xxx", apiSecret: "secret_xxx");

var documents = await recommand.Documents.GetDocumentsAsync(companyId);
```

`RecommandClient` is `IDisposable` because, in this constructor, it owns the
underlying `HttpClient`. Treat instances as long-lived (singleton-style); do
not create a new client per request.

## Authentication

Every call uses HTTP Basic authentication. Generate an API key and secret in
the [Recommand dashboard](https://app.recommand.eu/api-keys) — the key acts as
the username, the secret as the password.

JWT bearer and OAuth 2.0 are also supported by the API but not yet wired into
this client. If you need them, file an issue or supply your own
`DelegatingHandler` via the `IHttpClientBuilder` returned from
`AddRecommandClient`.

## Resource clients

`IRecommandClient` exposes one typed sub-client per OpenAPI tag:

| Property | Interface | What it covers |
|---|---|---|
| `Authentication` | `IAuthenticationClient` | Token verification |
| `Companies` | `ICompaniesClient` | Companies — top-level resource for organisations registered with Peppol |
| `CompanyDocumentTypes` | `ICompanyDocumentTypesClient` | Document types a company can send and receive |
| `CompanyIdentifiers` | `ICompanyIdentifiersClient` | Peppol identifiers attached to a company |
| `CompanyNotificationEmailAddresses` | `ICompanyNotificationEmailAddressesClient` | Email addresses notified about company-level events |
| `Customers` | `ICustomersClient` | Customers (recipients you have sent to) |
| `Documents` | `IDocumentsClient` | Listing, retrieval, download |
| `Labels` | `ILabelsClient` | Tagging |
| `Playgrounds` | `IPlaygroundsClient` | Sandbox testing environments |
| `Recipients` | `IRecipientsClient` | Peppol recipient lookups |
| `Sending` | `ISendingClient` | Sending Peppol documents |
| `Suppliers` | `ISuppliersClient` | Suppliers (parties that send to your companies) |
| `Webhooks` | `IWebhooksClient` | Webhook subscriptions and delivery |

Each interface is registered as a scoped DI service, so any one of them can be
replaced with a test double — register your fake before you call
`AddRecommandClient`:

```csharp
services.AddSingleton<ICompaniesClient>(myFakeCompaniesClient);
services.AddRecommandClient(o => { ... });
// MyFakeCompaniesClient wins for ICompaniesClient; the rest still come from the real root client.
```

## Configuration

```csharp
public sealed class RecommandClientOptions
{
    public string?  ApiKey    { get; set; }
    public string?  ApiSecret { get; set; }
    public string   BaseUrl   { get; set; } = "https://app.recommand.eu";
    public TimeSpan? Timeout  { get; set; }
}
```

Override `BaseUrl` only when targeting a non-production Recommand environment.

## Versioning

Pre-1.0 releases follow [SemVer](https://semver.org/) loosely — minor versions
may include breaking changes. Once the spec stabilises and the public surface
settles, the package will move to 1.0.0 and follow strict SemVer thereafter.

## License

[MIT](./LICENSE)

## Acknowledgements

Built and maintained by [Techworx](https://techworx.be), used in production at
[Akauntable](https://akauntable.com).
This is a third-party SDK — Recommand and the Recommand logo are trademarks of
their respective owners.
