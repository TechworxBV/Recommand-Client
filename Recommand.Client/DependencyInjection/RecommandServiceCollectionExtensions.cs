using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Recommand.Client;
using Recommand.Client.Authentication;

// Land the extension in Microsoft.Extensions.DependencyInjection so consumers
// who already have `using Microsoft.Extensions.DependencyInjection;` (which is
// the case for every ASP.NET Core app) don't need an extra `using Recommand.Client;`
// in Program.cs / Startup.cs to call AddRecommandClient.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering
/// <see cref="IRecommandClient"/>.
/// </summary>
public static class RecommandServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRecommandClient"/> with HTTP Basic authentication,
    /// plus each typed sub-client (<see cref="ICompaniesClient"/>,
    /// <see cref="IDocumentsClient"/>, etc.) as a scoped service that resolves
    /// to the corresponding property of the root client.
    /// </summary>
    /// <remarks>
    /// Registering each sub-client individually means consumers can inject
    /// just the resource client they need — e.g. <c>ctor(ICompaniesClient companies)</c>
    /// — and tests can mock that one interface without faking the whole root.
    /// The underlying <see cref="System.Net.Http.HttpClient"/> is managed by
    /// <see cref="System.Net.Http.IHttpClientFactory"/>; chain on the returned
    /// <see cref="IHttpClientBuilder"/> to add resilience, logging, or other
    /// message handlers.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddRecommandClient(o =>
    /// {
    ///     o.ApiKey    = builder.Configuration["Recommand:ApiKey"];
    ///     o.ApiSecret = builder.Configuration["Recommand:ApiSecret"];
    /// });
    ///
    /// // Then either:
    /// public class MyService(IRecommandClient recommand) { ... }
    /// // or, equivalently:
    /// public class MyService(ICompaniesClient companies) { ... }
    /// </code>
    /// </example>
    /// <summary>The named-HttpClient registration key used internally.</summary>
    private const string HttpClientName = "Recommand";

    public static IHttpClientBuilder AddRecommandClient(
        this IServiceCollection services,
        Action<RecommandClientOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));

        services.AddOptions<RecommandClientOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrEmpty(o.ApiKey), "Recommand ApiKey is required.")
            .Validate(o => !string.IsNullOrEmpty(o.ApiSecret), "Recommand ApiSecret is required.");

        services.AddTransient<BasicAuthenticationHandler>();

        // A NAMED HttpClient (not typed). The typed variant
        // (AddHttpClient<TInterface, TImpl>) registers TInterface as transient,
        // which would mean every IRecommandClient / ICompaniesClient / …
        // resolution creates its own RecommandClient — defeating the point of
        // injecting individual sub-clients, and producing surprising behaviour
        // where two injected sub-clients are backed by different roots.
        var builder = services
            .AddHttpClient(HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<RecommandClientOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                if (opts.Timeout is { } timeout)
                {
                    http.Timeout = timeout;
                }
            })
            .AddHttpMessageHandler<BasicAuthenticationHandler>();

        // Scoped IRecommandClient → one root per DI scope (= one per HTTP
        // request in ASP.NET Core). The HttpClient itself is still pooled by
        // IHttpClientFactory; only the thin wrapper is per-scope.
        services.TryAddScoped<IRecommandClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new RecommandClient(factory.CreateClient(HttpClientName));
        });

        // Each sub-client resolves to the corresponding property of the
        // (cached) scoped root, so all 13 share one HttpClient. TryAdd lets
        // consumers override any one with a test double by registering it
        // first, before they call AddRecommandClient.
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Authentication);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Companies);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().CompanyDocumentTypes);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().CompanyIdentifiers);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().CompanyNotificationEmailAddresses);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Customers);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Documents);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Labels);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Playgrounds);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Recipients);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Sending);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Suppliers);
        services.TryAddScoped(sp => sp.GetRequiredService<IRecommandClient>().Webhooks);

        return builder;
    }
}
