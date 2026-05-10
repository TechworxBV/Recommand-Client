using System.Text;
using System.Text.Json;
using Recommand.Client;
using Recommand.Client.Webhooks;
using Xunit;

namespace Recommand.Client.Tests;

public class WebhookPayloadTests
{
    [Fact]
    public void Parse_DocumentReceived_ReturnsTypedSubclass()
    {
        const string payload = """
            {
              "eventType": "document.received",
              "documentId": "doc_xxx",
              "teamId": "team_xxx",
              "companyId": "c_xxx"
            }
            """;

        var evt = WebhookPayload.Parse(payload);

        var typed = Assert.IsType<DocumentReceivedWebhook>(evt);
        Assert.Equal("doc_xxx", typed.DocumentId);
        Assert.Equal("team_xxx", typed.TeamId);
        Assert.Equal("c_xxx", typed.CompanyId);
        Assert.Equal(WebhookEventTypes.DocumentReceived, typed.EventType);
    }

    [Fact]
    public void Parse_DocumentLabelAssigned_ReturnsTypedSubclass()
    {
        const string payload = """
            {
              "eventType": "document.label.assigned",
              "documentId": "doc_xxx",
              "teamId": "team_xxx",
              "companyId": "c_xxx",
              "labelId": "lab_xxx"
            }
            """;

        var evt = WebhookPayload.Parse(payload);

        var typed = Assert.IsType<DocumentLabelAssignedWebhook>(evt);
        Assert.Equal("doc_xxx", typed.DocumentId);
        Assert.Equal("lab_xxx", typed.LabelId);
    }

    [Fact]
    public void Parse_CompanyVerification_ReturnsTypedSubclass()
    {
        const string payload = """
            {
              "eventType": "company.verification",
              "teamId": "team_xxx",
              "companyId": "c_xxx",
              "status": "verified"
            }
            """;

        var evt = WebhookPayload.Parse(payload);

        var typed = Assert.IsType<CompanyVerificationWebhook>(evt);
        Assert.Equal(CompanyVerificationWebhookStatus.Verified, typed.Status);
    }

    [Fact]
    public void Parse_UnknownEventType_ReturnsBasePayload_AndExposesEventType()
    {
        // Forward-compat: a future API release adds an event type this SDK
        // version doesn't have a typed subclass for. The base WebhookPayload
        // is returned with TeamId/CompanyId populated and the wire eventType
        // accessible via the EventType getter (sourced from AdditionalProperties).
        const string payload = """
            {
              "eventType": "document.delivered.future",
              "teamId": "team_xxx",
              "companyId": "c_xxx",
              "futureField": "preserved"
            }
            """;

        var evt = WebhookPayload.Parse(payload);

        Assert.NotNull(evt);
        Assert.Equal(typeof(WebhookPayload), evt!.GetType());
        Assert.Equal("team_xxx", evt.TeamId);
        Assert.Equal("c_xxx", evt.CompanyId);
        Assert.Equal("document.delivered.future", evt.EventType);
        // Additional unknown fields preserved for inspection.
        Assert.True(evt.AdditionalProperties.ContainsKey("futureField"));
    }

    [Fact]
    public async Task ParseAsync_FromStream_DispatchesPolymorphically()
    {
        const string payload = """
            {"eventType":"document.sent","documentId":"doc_yyy","teamId":"t","companyId":"c"}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var evt = await WebhookPayload.ParseAsync(stream);

        var typed = Assert.IsType<DocumentSentWebhook>(evt);
        Assert.Equal("doc_yyy", typed.DocumentId);
    }

    [Fact]
    public void EventTypeConstants_MatchWireValues()
    {
        // Ensures the constants in WebhookEventTypes match the discriminator
        // mapping in the spec. If a constant drifts from the wire format,
        // pattern-matching after a string comparison breaks silently.
        Assert.Equal("document.received",          WebhookEventTypes.DocumentReceived);
        Assert.Equal("document.sent",              WebhookEventTypes.DocumentSent);
        Assert.Equal("document.label.assigned",    WebhookEventTypes.DocumentLabelAssigned);
        Assert.Equal("document.label.unassigned",  WebhookEventTypes.DocumentLabelUnassigned);
        Assert.Equal("company.verification",       WebhookEventTypes.CompanyVerification);
    }
}
