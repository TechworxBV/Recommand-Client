using Xunit;

namespace Recommand.Client.Tests;

public class RecommandClientFacadeTests
{
    [Fact]
    public void Ctor_WithApiKeyAndSecret_ExposesAllResourceClients()
    {
        using var client = new RecommandClient("key_xxx", "secret_xxx");

        Assert.NotNull(client.Authentication);
        Assert.NotNull(client.Companies);
        Assert.NotNull(client.CompanyDocumentTypes);
        Assert.NotNull(client.CompanyIdentifiers);
        Assert.NotNull(client.CompanyNotificationEmailAddresses);
        Assert.NotNull(client.Customers);
        Assert.NotNull(client.Documents);
        Assert.NotNull(client.Labels);
        Assert.NotNull(client.Playgrounds);
        Assert.NotNull(client.Recipients);
        Assert.NotNull(client.Reporting);
        Assert.NotNull(client.Sending);
        Assert.NotNull(client.Suppliers);
        Assert.NotNull(client.Webhooks);
    }

    [Fact]
    public async Task Ctor_WithHttpClientBaseAddress_SendsRequestsThere()
    {
        var captured = new CapturingHandler();
        using var http = new HttpClient(captured) { BaseAddress = new Uri("https://staging.recommand.eu") };
        using var client = new RecommandClient(http);

        try { await client.Companies.GetCompanyAsync("c_xxx"); }
        catch (RecommandApiException) { /* CapturingHandler answers with an empty body */ }

        Assert.Equal("https://staging.recommand.eu/api/v1/companies/c_xxx", captured.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Ctor_WithHttpClientWithoutBaseAddress_DefaultsToProduction()
    {
        // Regression: the generated clients used to fall back to the spec's
        // server URL, which was http://localhost:3001.
        var captured = new CapturingHandler();
        using var http = new HttpClient(captured);
        using var client = new RecommandClient(http);

        try { await client.Authentication.VerifyAuthAsync(); }
        catch (RecommandApiException) { /* CapturingHandler answers with an empty body */ }

        Assert.Equal("https://app.recommand.eu/api/core/auth/verify", captured.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public void Ctor_WithNullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RecommandClient(httpClient: null!));
    }
}
