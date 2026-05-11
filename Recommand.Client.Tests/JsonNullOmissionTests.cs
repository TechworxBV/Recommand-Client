using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Recommand.Client;
using Xunit;

namespace Recommand.Client.Tests;

/// <summary>
/// Pins the SDK's serialization policy: nullable optional properties left
/// unset by the caller must not appear in the request body as
/// <c>"foo": null</c>. Regressing this would surprise the server and
/// bloat payloads.
/// </summary>
public class JsonNullOmissionTests
{
    [Fact]
    public async Task SendInvoiceRequest_OmitsUnsetOptionalProperties()
    {
        var captured = new CapturingHandler();
        using var http = new HttpClient(captured) { BaseAddress = new System.Uri("http://test.local") };
        var sending = new SendingClient(http);

        var request = new SendInvoiceRequest
        {
            Recipient = "0208:0123456789",
            Document = new SendInvoice
            {
                // intentionally leave most fields default — they're nullable in the SDK
            },
        };

        // Suppress server-side noise; we only care about the request body.
        try { await sending.SendDocumentAsync("c_xxx", request); }
        catch { /* expected — CapturingHandler returns a synthetic 200 with empty body */ }

        var body = captured.LastBody!;
        Assert.NotNull(body);

        // Nullable-and-unset optional properties must be absent from the wire.
        Assert.DoesNotContain("\"email\":null", body);
        Assert.DoesNotContain("\"pdfGeneration\":null", body);
        Assert.DoesNotContain("\"doctypeId\":null", body);
        Assert.DoesNotContain("\"processId\":null", body);
    }

    [Fact]
    public async Task SendInvoiceRequest_KeepsExplicitlySetProperties()
    {
        var captured = new CapturingHandler();
        using var http = new HttpClient(captured) { BaseAddress = new System.Uri("http://test.local") };
        var sending = new SendingClient(http);

        var request = new SendInvoiceRequest
        {
            Recipient = "0208:0123456789",
            Document = new SendInvoice(),
        };

        try { await sending.SendDocumentAsync("c_xxx", request); } catch { }

        var body = captured.LastBody!;
        Assert.Contains("\"recipient\":\"0208:0123456789\"", body);
        Assert.Contains("\"documentType\":\"invoice\"", body);  // injected by JsonInheritanceConverter
        Assert.Contains("\"document\":", body);
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
