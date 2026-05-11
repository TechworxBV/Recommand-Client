using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Recommand.Client;
using Xunit;

namespace Recommand.Client.Tests;

/// <summary>
/// Pins the wire format of enums whose values start with a digit
/// (Peppol scheme codes, country codes etc.). The C# names are
/// underscore-prefixed (<c>_0208</c>); the JSON values must be the raw
/// codes (<c>"0208"</c>). Regressing this would surprise the server.
/// </summary>
public class EnumMemberSerializationTests
{
    [Fact]
    public void EnterpriseNumberScheme_Serializes_AsWireValue()
    {
        var party = new Party
        {
            Name             = "Acme",
            EnterpriseNumber = "0735530511",
            EnterpriseNumberScheme = EnterpriseNumberScheme._0208,
        };

        var json = JsonSerializer.Serialize(party);

        Assert.Contains("\"enterpriseNumberScheme\":\"0208\"", json);
        Assert.DoesNotContain("\"enterpriseNumberScheme\":\"_0208\"", json);
    }

    [Fact]
    public void EnterpriseNumberScheme_Deserializes_FromWireValue()
    {
        const string json = """
            {"name":"Acme","enterpriseNumber":"0735530511","enterpriseNumberScheme":"0208"}
            """;

        var party = JsonSerializer.Deserialize<Party>(json);

        Assert.NotNull(party);
        Assert.Equal(EnterpriseNumberScheme._0208, party!.EnterpriseNumberScheme);
    }

    [Fact]
    public void EnterpriseNumberScheme_RoundTrips()
    {
        var original = new Party
        {
            Name = "Acme",
            EnterpriseNumber = "0735530511",
            EnterpriseNumberScheme = EnterpriseNumberScheme._0007,
        };

        var roundTripped = JsonSerializer.Deserialize<Party>(JsonSerializer.Serialize(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(original.EnterpriseNumberScheme, roundTripped!.EnterpriseNumberScheme);
    }

    [Fact]
    public void EmailWhen_Serializes_AsWireValue()
    {
        // Sanity check that enums whose C# names DON'T need underscore
        // prefixing also still serialize correctly (i.e. the new converter
        // didn't regress the easy cases).
        var email = new Email
        {
            When = EmailWhen.OnPeppolFailure,
        };

        var json = JsonSerializer.Serialize(email);

        Assert.Contains("\"when\":\"on_peppol_failure\"", json);
    }

    [Fact]
    public async Task SendInvoiceRequest_EnterpriseNumberScheme_OnWire_UsesRawCode()
    {
        // End-to-end: capture the actual HTTP body sent by the generated client.
        var captured = new CapturingHandler();
        using var http = new HttpClient(captured) { BaseAddress = new System.Uri("http://test.local") };
        var sending = new SendingClient(http);

        var request = new SendInvoiceRequest
        {
            Recipient = "0208:0123456789",
            Document  = new SendInvoice
            {
                Seller = new Party
                {
                    Name = "Acme NV",
                    EnterpriseNumber = "0735530511",
                    EnterpriseNumberScheme = EnterpriseNumberScheme._0208,
                },
            },
        };

        try { await sending.SendDocumentAsync("c_xxx", request); } catch { }

        var body = captured.LastBody!;
        Assert.Contains("\"enterpriseNumberScheme\":\"0208\"", body);
        Assert.DoesNotContain("_0208", body);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync();
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
