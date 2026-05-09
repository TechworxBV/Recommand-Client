using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Recommand.Client;
using Recommand.Client.Authentication;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// <see cref="IServiceCollection"/> extensions for registering
/// <see cref="IRecommandClient"/> and its typed sub-clients.
/// </summary>
public static class RecommandServiceCollectionExtensions
{
    private const string HttpClientName = "Recommand";

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
    /// The underlying <see cref="HttpClient"/> is managed by
    /// <see cref="IHttpClientFactory"/>; chain on the returned
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

        services.TryAddScoped<IRecommandClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new RecommandClient(factory.CreateClient(HttpClientName));
        });

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
