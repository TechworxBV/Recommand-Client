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
}
