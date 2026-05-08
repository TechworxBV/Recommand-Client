using System.Net;
using Microsoft.Extensions.Options;

namespace Recommand.Client.Tests;

/// <summary>
/// Captures the most recent <see cref="HttpRequestMessage"/> that flowed
/// through the handler chain and returns a canned 200 response. Used to
/// inspect headers that <see cref="Recommand.Client.Authentication.BasicAuthenticationHandler"/>
/// sets without making real network calls.
/// </summary>
internal sealed class CapturingHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

/// <summary>
/// Minimal <see cref="IOptionsMonitor{T}"/> for tests. Exposes a setter so
/// individual tests can simulate runtime credential rotation.
/// </summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public TestOptionsMonitor(T initial) => CurrentValue = initial;

    public T CurrentValue { get; private set; }

    public void SetValue(T newValue) => CurrentValue = newValue;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
