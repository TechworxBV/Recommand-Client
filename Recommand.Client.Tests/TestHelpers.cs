using System.Net;
using Microsoft.Extensions.Options;

namespace Recommand.Client.Tests;

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

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public TestOptionsMonitor(T initial) => CurrentValue = initial;

    public T CurrentValue { get; private set; }

    public void SetValue(T newValue) => CurrentValue = newValue;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
