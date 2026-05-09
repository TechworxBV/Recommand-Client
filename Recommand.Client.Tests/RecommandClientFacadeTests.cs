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
        Assert.NotNull(client.Sending);
        Assert.NotNull(client.Suppliers);
        Assert.NotNull(client.Webhooks);
    }

    [Fact]
    public void Ctor_WithCustomBaseUrl_PropagatesToAllTypedClients()
    {
        const string expected = "https://staging.recommand.eu/";

        using var http = new HttpClient { BaseAddress = new Uri("https://staging.recommand.eu") };
        using var client = new RecommandClient(http);

        Assert.Equal(expected, ((CompaniesClient)client.Companies).BaseUrl);
        Assert.Equal(expected, ((DocumentsClient)client.Documents).BaseUrl);
        Assert.Equal(expected, ((WebhooksClient)client.Webhooks).BaseUrl);
        Assert.Equal(expected, ((CompanyIdentifiersClient)client.CompanyIdentifiers).BaseUrl);
    }

    [Fact]
    public void Ctor_WithNullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RecommandClient(httpClient: null!));
    }
}
