using System;
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
    /// Registers <see cref="IRecommandClient"/> with HTTP Basic authentication.
    /// The underlying <see cref="System.Net.Http.HttpClient"/> is managed by
    /// <see cref="System.Net.Http.IHttpClientFactory"/>; chain on the returned
    /// <see cref="IHttpClientBuilder"/> to add resilience, logging, or other
    /// message handlers.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddRecommandClient(o =>
    /// {
    ///     o.ApiKey    = builder.Configuration["Recommand:ApiKey"];
    ///     o.ApiSecret = builder.Configuration["Recommand:ApiSecret"];
    /// });
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

        return services
            .AddHttpClient<IRecommandClient, RecommandClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<RecommandClientOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                if (opts.Timeout is { } timeout)
                {
                    http.Timeout = timeout;
                }
            })
            .AddHttpMessageHandler<BasicAuthenticationHandler>();
    }
}
