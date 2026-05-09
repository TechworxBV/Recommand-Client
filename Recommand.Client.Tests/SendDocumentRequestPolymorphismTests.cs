using System.Text.Json;
using Xunit;

namespace Recommand.Client.Tests;

public class SendDocumentRequestPolymorphismTests
{
    [Fact]
    public void Serializing_SendInvoiceRequest_AsBaseType_WritesDocumentTypeDiscriminator()
    {
        SendDocumentRequest request = new SendInvoiceRequest
        {
            Recipient = "0208:987654321",
            Document = new SendInvoice(),
        };

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"documentType\":\"invoice\"", json);
        Assert.Contains("\"recipient\":\"0208:987654321\"", json);
        Assert.Contains("\"document\":", json);
    }

    [Fact]
    public void Serializing_SendCreditNoteRequest_WritesCorrectDiscriminator()
    {
        SendDocumentRequest request = new SendCreditNoteRequest
        {
            Recipient = "0208:111111111",
            Document = new SendCreditNote(),
        };

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"documentType\":\"creditNote\"", json);
    }

    [Fact]
    public void Deserializing_RoundtripsThroughTheBaseType()
    {
        SendDocumentRequest original = new SendInvoiceRequest
        {
            Recipient = "0208:987654321",
            Document = new SendInvoice(),
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<SendDocumentRequest>(json);

        Assert.IsType<SendInvoiceRequest>(roundTripped);
        Assert.Equal("0208:987654321", roundTripped!.Recipient);
    }

    [Fact]
    public void Serializing_SendXmlRequest_WritesXmlDiscriminator()
    {
        SendDocumentRequest request = new SendXmlRequest
        {
            Recipient = "0208:222222222",
            Document = "<Invoice/>",
        };

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"documentType\":\"xml\"", json);
    }

    [Fact]
    public void AllSixVariants_SerializeWithCorrectDiscriminator()
    {
        var cases = new (SendDocumentRequest Request, string ExpectedDiscriminator)[]
        {
            (new SendInvoiceRequest                { Recipient = "x", Document = new SendInvoice()                },  "invoice"),
            (new SendCreditNoteRequest             { Recipient = "x", Document = new SendCreditNote()             },  "creditNote"),
            (new SendSelfBillingInvoiceRequest     { Recipient = "x", Document = new SendSelfBillingInvoice()     },  "selfBillingInvoice"),
            (new SendSelfBillingCreditNoteRequest  { Recipient = "x", Document = new SendSelfBillingCreditNote()  },  "selfBillingCreditNote"),
            (new SendMessageLevelResponseRequest   { Recipient = "x", Document = new SendMessageLevelResponse()   },  "messageLevelResponse"),
            (new SendXmlRequest                    { Recipient = "x", Document = "<Invoice/>"                     },  "xml"),
        };

        foreach (var (request, expectedDiscriminator) in cases)
        {
            var json = JsonSerializer.Serialize(request);
            Assert.Contains($"\"documentType\":\"{expectedDiscriminator}\"", json);
        }
    }
}
