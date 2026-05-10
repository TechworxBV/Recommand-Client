using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Recommand.Client.Webhooks;

namespace Recommand.Client.AspNetCore;

/// <summary>
/// Register a webhook receiver endpoint in one line.
/// </summary>
public static class WebhookEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Register a Recommand webhook receiver endpoint at <paramref name="pattern"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per delivery the endpoint:
    /// </para>
    /// <list type="number">
    ///   <item>Reads the raw request body bytes (capped by
    ///         <see cref="RecommandWebhookOptions.MaxBodyBytes"/>).</item>
    ///   <item>If a signing secret is configured, verifies the
    ///         <c>X-Signature</c> HMAC-SHA256 header in constant time.
    ///         Rejects with <c>401</c> on mismatch (and on absent headers when
    ///         <see cref="RecommandWebhookOptions.RequireSignatureWhenSecretConfigured"/>
    ///         is <c>true</c>).</item>
    ///   <item>If an <see cref="IWebhookDeduplicator"/> is registered, looks up
    ///         the <c>X-Idempotency-Key</c>. If previously seen, returns
    ///         <c>200 OK</c> immediately without invoking the handler.</item>
    ///   <item>Parses the body via <see cref="WebhookPayload.Parse(string?, System.Text.Json.JsonSerializerOptions?)"/>
    ///         (forward-compat — unknown event types arrive as base
    ///         <see cref="WebhookPayload"/>).</item>
    ///   <item>Invokes <paramref name="handler"/>, passing the parsed payload
    ///         plus delivery metadata.</item>
    ///   <item>Returns <c>200 OK</c> on handler success, <c>500</c> on
    ///         exception (which triggers the API's retry behaviour).</item>
    /// </list>
    /// <para>
    /// Options resolution order: explicit <paramref name="options"/> argument
    /// &gt; <c>IOptions&lt;RecommandWebhookOptions&gt;</c> from DI &gt; defaults.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRecommandWebhooks(o =>
    /// {
    ///     o.SigningSecret = builder.Configuration["Recommand:Webhooks:Secret"];
    /// });
    /// builder.Services.AddSingleton&lt;IWebhookDeduplicator, InMemoryWebhookDeduplicator&gt;();
    ///
    /// app.MapRecommandWebhook("/webhooks/recommand", async delivery =>
    /// {
    ///     switch (delivery.Payload)
    ///     {
    ///         case DocumentReceivedWebhook d:
    ///             await ProcessReceivedAsync(d, delivery.Http.RequestAborted);
    ///             break;
    ///         case CompanyVerificationWebhook v:
    ///             logger.LogInformation("Company {Id} verification: {Status}",
    ///                 v.CompanyId, v.Status);
    ///             break;
    ///         default:
    ///             logger.LogWarning("Unknown event type: {EventType}",
    ///                 delivery.Payload.EventType);
    ///             break;
    ///     }
    /// });
    /// </code>
    /// </example>
    public static IEndpointConventionBuilder MapRecommandWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<WebhookDelivery, Task> handler,
        RecommandWebhookOptions? options = null)
    {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        if (pattern is null) throw new ArgumentNullException(nameof(pattern));
        if (handler is null) throw new ArgumentNullException(nameof(handler));

        return endpoints.MapPost(pattern, ctx => HandleAsync(ctx, handler, options));
    }

    private static async Task HandleAsync(
        HttpContext ctx,
        Func<WebhookDelivery, Task> handler,
        RecommandWebhookOptions? inlineOptions)
    {
        var opts = inlineOptions
                   ?? ctx.RequestServices.GetService<IOptions<RecommandWebhookOptions>>()?.Value
                   ?? new RecommandWebhookOptions();

        var logger = ctx.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Recommand.Webhooks")
                     ?? NullLogger.Instance;

        // 1. Read body, capped.
        byte[] body;
        try
        {
            body = await ReadBodyAsync(ctx.Request, opts.MaxBodyBytes, ctx.RequestAborted).ConfigureAwait(false);
        }
        catch (BodyTooLargeException)
        {
            logger.LogWarning("Webhook body exceeded {MaxBytes} bytes; rejecting with 413.", opts.MaxBodyBytes);
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        // 2. Signature verification.
        var signatureHeader = ctx.Request.Headers[WebhookSignature.SignatureHeader].ToString();
        if (!string.IsNullOrEmpty(opts.SigningSecret))
        {
            if (string.IsNullOrEmpty(signatureHeader))
            {
                if (opts.RequireSignatureWhenSecretConfigured)
                {
                    logger.LogWarning("Webhook delivery missing {Header}; rejecting with 401.", WebhookSignature.SignatureHeader);
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                // else: secret configured but signature header absent and we're lenient
            }
            else if (!WebhookSignature.Verify(body, signatureHeader, opts.SigningSecret!))
            {
                logger.LogWarning("Webhook signature verification failed; rejecting with 401.");
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        // 3. Idempotency.
        var idempotencyKey = ctx.Request.Headers[WebhookSignature.IdempotencyHeader].ToString();
        var dedup = ctx.RequestServices.GetService<IWebhookDeduplicator>();
        if (dedup is not null && !string.IsNullOrEmpty(idempotencyKey))
        {
            var fresh = await dedup.TryRegisterAsync(idempotencyKey, ctx.RequestAborted).ConfigureAwait(false);
            if (!fresh)
            {
                logger.LogDebug("Webhook delivery {Key} already processed; ack-only.", idempotencyKey);
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }
        }

        // 4. Parse.
        WebhookPayload? payload;
        try
        {
            payload = WebhookPayload.Parse(System.Text.Encoding.UTF8.GetString(body));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Webhook body is not valid JSON; rejecting with 400.");
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (payload is null)
        {
            logger.LogWarning("Webhook body parsed to null; rejecting with 400.");
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // 5. Dispatch.
        await handler(new WebhookDelivery(payload, idempotencyKey, ctx)).ConfigureAwait(false);

        // 6. Default ack.
        if (!ctx.Response.HasStarted) ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, int maxBytes, System.Threading.CancellationToken ct)
    {
        // Buffer first so signature verification can be done against the same
        // bytes the model would parse. Cap with a hand-rolled loop rather than
        // trusting Content-Length.
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + read > maxBytes) throw new BodyTooLargeException();
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private sealed class BodyTooLargeException : Exception { }
}
