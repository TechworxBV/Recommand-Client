using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Recommand.Client.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddRecommandClient_RegistersIRecommandClient()
    {
        var services = new ServiceCollection();
        services.AddRecommandClient(o =>
        {
            o.ApiKey = "key_xxx";
            o.ApiSecret = "secret_xxx";
        });

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IRecommandClient>();

        Assert.NotNull(client);
        Assert.NotNull(client.Documents);
        Assert.NotNull(client.Companies);
    }

    [Fact]
    public void AddRecommandClient_AppliesCustomBaseUrl()
    {
        // NSwag's BaseUrl setter normalises to always end with "/".
        const string expected = "https://staging.recommand.eu/";

        var services = new ServiceCollection();
        services.AddRecommandClient(o =>
        {
            o.ApiKey = "key_xxx";
            o.ApiSecret = "secret_xxx";
            o.BaseUrl = "https://staging.recommand.eu";
        });

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IRecommandClient>();

        Assert.Equal(expected, ((DocumentsClient)client.Documents).BaseUrl);
    }

    [Fact]
    public void AddRecommandClient_RejectsMissingApiKey()
    {
        var services = new ServiceCollection();
        services.AddRecommandClient(o =>
        {
            o.ApiKey = null;
            o.ApiSecret = "secret_xxx";
        });

        using var sp = services.BuildServiceProvider();

        // Resolving the client triggers the IOptions factory, which evaluates
        // the validators we registered in AddRecommandClient.
        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IRecommandClient>());
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void AddRecommandClient_RejectsMissingApiSecret()
    {
        var services = new ServiceCollection();
        services.AddRecommandClient(o =>
        {
            o.ApiKey = "key_xxx";
            o.ApiSecret = null;
        });

        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IRecommandClient>());
        Assert.Contains("ApiSecret", ex.Message);
    }

    [Fact]
    public void AddRecommandClient_RejectsNullConfigure()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddRecommandClient(null!));
    }

    [Fact]
    public void AddRecommandClient_RegistersEachSubClientIndividually()
    {
        // Consumers should be able to inject e.g. ICompaniesClient directly
        // rather than IRecommandClient — that lets them mock just the
        // sub-client they care about in tests.
        var services = new ServiceCollection();
        services.AddRecommandClient(o =>
        {
            o.ApiKey = "key_xxx";
            o.ApiSecret = "secret_xxx";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var scoped = scope.ServiceProvider;

        Assert.NotNull(scoped.GetRequiredService<IAuthenticationClient>());
        Assert.NotNull(scoped.GetRequiredService<ICompaniesClient>());
        Assert.NotNull(scoped.GetRequiredService<ICompanyDocumentTypesClient>());
        Assert.NotNull(scoped.GetRequiredService<ICompanyIdentifiersClient>());
        Assert.NotNull(scoped.GetRequiredService<ICompanyNotificationEmailAddressesClient>());
        Assert.NotNull(scoped.GetRequiredService<ICustomersClient>());
        Assert.NotNull(scoped.GetRequiredService<IDocumentsClient>());
        Assert.NotNull(scoped.GetRequiredService<ILabelsClient>());
        Assert.NotNull(scoped.GetRequiredService<IPlaygroundsClient>());
        Assert.NotNull(scoped.GetRequiredService<IRecipientsClient>());
        Assert.NotNull(scoped.GetRequiredService<ISendingClient>());
        Assert.NotNull(scoped.GetRequiredService<ISuppliersClient>());
        Assert.NotNull(scoped.GetRequiredService<IWebhooksClient>());
    }

    [Fact]
    public void AddRecommandClient_SubClientsShareTheSameRootInstance()
    {
        // Within a single scope, each sub-client should be backed by the
        // same root RecommandClient — otherwise we'd be paying for 13
        // independent HttpClient lifetimes per request.
        var services = new ServiceCollection();
        services.AddRecommandClient(o =>
        {
            o.ApiKey = "key_xxx";
            o.ApiSecret = "secret_xxx";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var scoped = scope.ServiceProvider;

        var root = scoped.GetRequiredService<IRecommandClient>();
        Assert.Same(root.Companies, scoped.GetRequiredService<ICompaniesClient>());
        Assert.Same(root.Documents, scoped.GetRequiredService<IDocumentsClient>());
    }

    [Fact]
    public void AddRecommandClient_AllowsOverridingASingleSubClient()
    {
        // TryAdd semantics let consumers replace any sub-client (typically
        // with a test double) by registering their own first. We use a real
        // CompaniesClient instance as a stand-in marker — the test verifies
        // identity, not behaviour.
        using var marker = new HttpClient();
        var preRegistered = new CompaniesClient(marker);

        var services = new ServiceCollection();
        services.AddSingleton<ICompaniesClient>(preRegistered);   // registered before AddRecommandClient
        services.AddRecommandClient(o =>
        {
            o.ApiKey = "key_xxx";
            o.ApiSecret = "secret_xxx";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.Same(preRegistered, scope.ServiceProvider.GetRequiredService<ICompaniesClient>());
    }
}
