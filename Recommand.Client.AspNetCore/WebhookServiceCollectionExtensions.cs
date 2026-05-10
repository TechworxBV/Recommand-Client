using System;
using Microsoft.Extensions.DependencyInjection;

namespace Recommand.Client.AspNetCore;

/// <summary>
/// DI registration for <see cref="RecommandWebhookOptions"/> so endpoints
/// registered via <c>MapRecommandWebhook</c> can pick up signing secret,
/// max body size, etc. from configuration without restating them at the
/// endpoint registration site.
/// </summary>
public static class WebhookServiceCollectionExtensions
{
    /// <summary>
    /// Register webhook receiver options. After this, any endpoint registered
    /// via <c>MapRecommandWebhook</c> without inline options will pick up
    /// these values from DI.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddRecommandWebhooks(o =>
    /// {
    ///     o.SigningSecret = builder.Configuration["Recommand:Webhooks:Secret"];
    /// });
    /// // Optional but recommended for production:
    /// builder.Services.AddSingleton&lt;IWebhookDeduplicator, MyRedisDeduplicator&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddRecommandWebhooks(
        this IServiceCollection services,
        Action<RecommandWebhookOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<RecommandWebhookOptions>();
        }

        return services;
    }
}
