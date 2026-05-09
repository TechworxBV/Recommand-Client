using System.Text;
using Recommand.Client.Authentication;
using Xunit;

namespace Recommand.Client.Tests;

public class BasicAuthenticationHandlerTests
{
    [Fact]
    public async Task SendAsync_AddsBasicAuthHeader_FromExplicitCredentials()
    {
        var captured = new CapturingHandler();
        using var handler = new BasicAuthenticationHandler("key_xxx", "secret_xxx") { InnerHandler = captured };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await http.GetAsync("/foo");

        var auth = captured.LastRequest?.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("key_xxx:secret_xxx"));
        Assert.Equal(expected, auth.Parameter);
    }

    [Fact]
    public async Task SendAsync_ReadsCredentialsFromOptionsMonitor_OnEachRequest()
    {
        var options = new TestOptionsMonitor<RecommandClientOptions>(new RecommandClientOptions
        {
            ApiKey = "key_v1",
            ApiSecret = "secret_v1",
        });
        var captured = new CapturingHandler();
        using var handler = new BasicAuthenticationHandler(options) { InnerHandler = captured };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        await http.GetAsync("/foo");
        var first = captured.LastRequest!.Headers.Authorization!.Parameter;

        options.SetValue(new RecommandClientOptions { ApiKey = "key_v2", ApiSecret = "secret_v2" });

        await http.GetAsync("/foo");
        var second = captured.LastRequest!.Headers.Authorization!.Parameter;

        Assert.NotEqual(first, second);
        var expectedSecond = Convert.ToBase64String(Encoding.UTF8.GetBytes("key_v2:secret_v2"));
        Assert.Equal(expectedSecond, second);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenApiKeyIsMissing()
    {
        var options = new TestOptionsMonitor<RecommandClientOptions>(new RecommandClientOptions
        {
            ApiKey = null,
            ApiSecret = "secret_xxx",
        });
        using var handler = new BasicAuthenticationHandler(options) { InnerHandler = new CapturingHandler() };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync("/foo"));
        Assert.Contains(nameof(RecommandClientOptions.ApiKey), ex.Message);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenApiSecretIsMissing()
    {
        var options = new TestOptionsMonitor<RecommandClientOptions>(new RecommandClientOptions
        {
            ApiKey = "key_xxx",
            ApiSecret = null,
        });
        using var handler = new BasicAuthenticationHandler(options) { InnerHandler = new CapturingHandler() };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync("/foo"));
        Assert.Contains(nameof(RecommandClientOptions.ApiSecret), ex.Message);
    }
}
