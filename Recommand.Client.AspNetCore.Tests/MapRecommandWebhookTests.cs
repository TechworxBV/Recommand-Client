using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Recommand.Client;
using Recommand.Client.AspNetCore;
using Recommand.Client.Webhooks;
using Xunit;

namespace Recommand.Client.AspNetCore.Tests;

public class MapRecommandWebhookTests
{
    private const string Secret = "whsec_unit_test_secret";
    private const string DocumentReceivedJson =
        """{"eventType":"document.received","documentId":"doc_xxx","teamId":"t","companyId":"c"}""";

    [Fact]
    public async Task UnsignedDelivery_PassesWhenNoSecretConfigured_AndDispatches()
    {
        WebhookPayload? captured = null;

        using var server = BuildServer(
            handler: d => { captured = d.Payload; return Task.CompletedTask; });

        var resp = await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_1");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.IsType<DocumentReceivedWebhook>(captured);
    }

    [Fact]
    public async Task SignedDelivery_ValidSignature_Dispatches()
    {
        WebhookPayload? captured = null;

        using var server = BuildServer(
            handler: d => { captured = d.Payload; return Task.CompletedTask; },
            options: new RecommandWebhookOptions { SigningSecret = Secret });

        var sig = WebhookSignature.Compute(Encoding.UTF8.GetBytes(DocumentReceivedJson), Secret);
        var resp = await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_2", signature: sig);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.IsType<DocumentReceivedWebhook>(captured);
    }

    [Fact]
    public async Task SignedDelivery_TamperedBody_Rejected401()
    {
        var handlerCalled = false;
        using var server = BuildServer(
            handler: _ => { handlerCalled = true; return Task.CompletedTask; },
            options: new RecommandWebhookOptions { SigningSecret = Secret });

        // Compute signature over original, then send tampered.
        var sig = WebhookSignature.Compute(Encoding.UTF8.GetBytes(DocumentReceivedJson), Secret);
        var tampered = DocumentReceivedJson.Replace("doc_xxx", "doc_yyy");
        var resp = await PostAsync(server, tampered, idempotencyKey: "del_3", signature: sig);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task SignedDelivery_MissingSignature_Rejected401_WhenStrict()
    {
        using var server = BuildServer(
            handler: _ => Task.CompletedTask,
            options: new RecommandWebhookOptions
            {
                SigningSecret = Secret,
                RequireSignatureWhenSecretConfigured = true,
            });

        var resp = await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_4");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SignedDelivery_MissingSignature_PassesWhenLenient()
    {
        var handlerCalled = false;
        using var server = BuildServer(
            handler: _ => { handlerCalled = true; return Task.CompletedTask; },
            options: new RecommandWebhookOptions
            {
                SigningSecret = Secret,
                RequireSignatureWhenSecretConfigured = false,
            });

        var resp = await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_5");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task ReplayedDelivery_SameIdempotencyKey_ShortCircuited()
    {
        var calls = 0;
        var dedup = new InMemoryWebhookDeduplicator();

        using var server = BuildServer(
            handler: _ => { calls++; return Task.CompletedTask; },
            configureServices: s => s.AddSingleton<IWebhookDeduplicator>(dedup));

        const string key = "del_replay";
        var first  = await PostAsync(server, DocumentReceivedJson, idempotencyKey: key);
        var second = await PostAsync(server, DocumentReceivedJson, idempotencyKey: key);
        var third  = await PostAsync(server, DocumentReceivedJson, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(1, calls);  // handler invoked exactly once
    }

    [Fact]
    public async Task DistinctIdempotencyKeys_AllInvokeHandler()
    {
        var calls = 0;
        var dedup = new InMemoryWebhookDeduplicator();

        using var server = BuildServer(
            handler: _ => { calls++; return Task.CompletedTask; },
            configureServices: s => s.AddSingleton<IWebhookDeduplicator>(dedup));

        await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_A");
        await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_B");
        await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_C");

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task MalformedJson_Rejected400()
    {
        using var server = BuildServer(handler: _ => Task.CompletedTask);

        var resp = await PostAsync(server, "{not json", idempotencyKey: "del_bad");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UnknownEventType_DispatchesAsBasePayload()
    {
        WebhookPayload? captured = null;

        using var server = BuildServer(
            handler: d => { captured = d.Payload; return Task.CompletedTask; });

        const string future = """{"eventType":"document.delivered.future","teamId":"t","companyId":"c","futureField":"x"}""";
        var resp = await PostAsync(server, future, idempotencyKey: "del_future");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(captured);
        // Forward-compat: lands as base type, not throws.
        Assert.Equal(typeof(WebhookPayload), captured!.GetType());
        Assert.Equal("document.delivered.future", captured.EventType);
    }

    [Fact]
    public async Task BodyTooLarge_Rejected413()
    {
        using var server = BuildServer(
            handler: _ => Task.CompletedTask,
            options: new RecommandWebhookOptions { MaxBodyBytes = 256 });

        var huge = new string('x', 1000);
        var resp = await PostAsync(server, $"{{\"junk\":\"{huge}\"}}", idempotencyKey: "del_huge");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
    }

    [Fact]
    public async Task DeliveryContext_CarriesIdempotencyKey()
    {
        string? observedKey = null;

        using var server = BuildServer(
            handler: d => { observedKey = d.IdempotencyKey; return Task.CompletedTask; });

        await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_inspect");

        Assert.Equal("del_inspect", observedKey);
    }

    [Fact]
    public async Task OptionsFromDi_PickedUpAutomatically()
    {
        WebhookPayload? captured = null;

        using var server = BuildServer(
            handler: d => { captured = d.Payload; return Task.CompletedTask; },
            configureServices: s => s.AddRecommandWebhooks(o => o.SigningSecret = Secret));

        // Without explicit inline options, the endpoint reads the secret from DI.
        var sig = WebhookSignature.Compute(Encoding.UTF8.GetBytes(DocumentReceivedJson), Secret);
        var resp = await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_di", signature: sig);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.IsType<DocumentReceivedWebhook>(captured);

        // And the unsigned variant is now rejected.
        var resp2 = await PostAsync(server, DocumentReceivedJson, idempotencyKey: "del_di_2");
        Assert.Equal(HttpStatusCode.Unauthorized, resp2.StatusCode);
    }

    // ---------- helpers ----------

    private static TestServer BuildServer(
        Func<WebhookDelivery, Task> handler,
        RecommandWebhookOptions? options = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(s =>
            {
                s.AddRouting();
                configureServices?.Invoke(s);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapRecommandWebhook("/wh", handler, options));
            });
        return new TestServer(builder);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        TestServer server,
        string body,
        string idempotencyKey,
        string? signature = null)
    {
        var client = server.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/wh")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation(WebhookSignature.IdempotencyHeader, idempotencyKey);
        if (signature is not null)
            req.Headers.TryAddWithoutValidation(WebhookSignature.SignatureHeader, signature);
        return await client.SendAsync(req);
    }
}
