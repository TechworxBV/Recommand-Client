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
    public void Parse_UnknownEventType_ReturnsUnknownPayload_AndExposesEventType()
    {
        // Forward-compat: a future API release adds an event type this SDK
        // version doesn't have a typed subclass for.
        const string payload = """
            {
              "eventType": "document.delivered.future",
              "teamId": "team_xxx",
              "companyId": "c_xxx",
              "futureField": "preserved"
            }
            """;

        var evt = WebhookPayload.Parse(payload);

        var unknown = Assert.IsType<UnknownWebhookPayload>(evt);
        Assert.Equal("team_xxx", unknown.TeamId);
        Assert.Equal("c_xxx", unknown.CompanyId);
        Assert.Equal("document.delivered.future", unknown.EventType);
        Assert.True(unknown.AdditionalProperties.ContainsKey("futureField"));
    }

    [Fact]
    public void Parse_MissingEventType_ReturnsUnknownPayload()
    {
        var evt = WebhookPayload.Parse("""{"teamId":"t","companyId":"c"}""");

        var unknown = Assert.IsType<UnknownWebhookPayload>(evt);
        Assert.Null(unknown.EventType);
    }

    [Fact]
    public void Parse_DeliveryStatusChanged_ReturnsTypedSubclass()
    {
        const string payload = """
            {
              "eventType": "document.delivery_status_changed",
              "documentId": "doc_xxx",
              "teamId": "team_xxx",
              "companyId": "c_xxx",
              "deliveryId": "del_xxx",
              "channel": "peppol",
              "address": "0208:987654321",
              "status": "failed",
              "previousStatus": "pending",
              "failure": { "category": "recipient_not_found", "message": "Not registered", "providerCode": null }
            }
            """;

        var typed = Assert.IsType<DocumentDeliveryStatusChangedWebhook>(WebhookPayload.Parse(payload));

        Assert.Equal(DeliveryStatus.Failed, typed.Status);
        Assert.Equal(DeliveryStatus.Pending, typed.PreviousStatus);
        Assert.Equal(DeliveryFailureCategory.RecipientNotFound, typed.Failure!.Category);
        Assert.Equal(WebhookEventTypes.DocumentDeliveryStatusChanged, typed.EventType);
    }

    [Fact]
    public void Parse_ReportingStatusChanged_ReturnsTypedSubclass()
    {
        const string payload = """
            {
              "eventType": "document.reporting_status_changed",
              "documentId": "doc_xxx",
              "teamId": "team_xxx",
              "companyId": "c_xxx",
              "reportingStatus": "filed",
              "previousReportingStatus": null,
              "periodEnd": "2026-09-30",
              "submissionId": null,
              "outcomeCode": null,
              "previousOutcomeCode": null,
              "final": false
            }
            """;

        var typed = Assert.IsType<DocumentReportingStatusChangedWebhook>(WebhookPayload.Parse(payload));

        Assert.Equal("filed", typed.ReportingStatus);
        Assert.Equal("2026-09-30", typed.PeriodEnd);
    }

    [Fact]
    public void EventTypeConstants_CoverEveryGeneratedSubtype()
    {
        var constants = typeof(WebhookEventTypes).GetFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(
            JsonInheritanceConverter<WebhookPayload>.KnownDiscriminators.OrderBy(v => v, StringComparer.Ordinal),
            constants);
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
