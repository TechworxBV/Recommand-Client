using System.Text.Json;
using Xunit;

namespace Recommand.Client.Tests;

public class DocumentPolymorphismTests
{
    private const string Common = """
        "id":"doc_1","teamId":"team_1","companyId":"c_1","direction":"incoming",
        "senderId":"0208:1","receiverId":null,"docTypeId":"d","processId":"p","countryC1":"BE",
        "readAt":null,"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"
        """;

    [Fact]
    public void Document_WithKnownType_DeserialisesToTypedSubclassWithParsedPayload()
    {
        var json = "{" + Common + ""","type":"creditNote","parsed":{"creditNoteNumber":"CN-1"}}""";

        var document = JsonSerializer.Deserialize<TransmittedDocument>(json);

        var creditNote = Assert.IsType<TransmittedDocumentCreditNote>(document);
        Assert.Equal("doc_1", creditNote.Id);
        Assert.Equal(DocumentDirection.Incoming, creditNote.Direction);
        Assert.Equal("CN-1", creditNote.Parsed.CreditNoteNumber);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("aTypeAddedAfterThisSdkWasBuilt")]
    public void Document_WithUnmappedType_DeserialisesToUnknownSubclassInsteadOfThrowing(string type)
    {
        var json = "{" + Common + $$""","type":"{{type}}","parsed":null}""";

        var document = JsonSerializer.Deserialize<TransmittedDocumentSummary>(json);

        var unknown = Assert.IsType<UnknownTransmittedDocumentSummary>(document);
        Assert.Equal("doc_1", unknown.Id);
        Assert.Equal(type, JsonInheritanceConverter<TransmittedDocumentSummary>.GetDiscriminator(unknown));
    }

    [Fact]
    public void UnknownSubclass_RoundTripsItsDiscriminator()
    {
        var json = "{" + Common + ""","type":"brandNew","parsed":null}""";
        var document = JsonSerializer.Deserialize<TransmittedDocument>(json)!;

        var written = JsonSerializer.Serialize(document);

        Assert.Contains("\"type\":\"brandNew\"", written);
        Assert.DoesNotContain("UnknownTransmittedDocument", written);
    }

    [Fact]
    public void Serializing_PolymorphicBaseItself_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(new SendDocumentRequest()));
    }

    [Fact]
    public void Vat_AcceptsBothVariants_AndReadsBackByShape()
    {
        var auto = new SendInvoice { Vat = new VatTotalsAutoCalculation() };
        var totals = new SendInvoice { Vat = new VatTotals { TotalVatAmount = "21.00" } };

        Assert.IsType<VatTotalsAutoCalculation>(
            JsonSerializer.Deserialize<SendInvoice>(JsonSerializer.Serialize(auto))!.Vat);
        Assert.IsType<VatTotals>(
            JsonSerializer.Deserialize<SendInvoice>(JsonSerializer.Serialize(totals))!.Vat);
    }

    [Fact]
    public void FrenchB2BiPaymentReport_HasSeparateSubmitAndStoredTypes()
    {
        // The spec uses one schema as a variant of two unions; the generator
        // gives the stored union its own copy.
        Assert.IsAssignableFrom<FrenchB2BiReport>(new FrenchB2BiPaymentReport());
        Assert.IsAssignableFrom<StoredFrenchB2BiReport>(new StoredFrenchB2BiPaymentReport());

        var json = JsonSerializer.Serialize<FrenchB2BiReport>(new FrenchB2BiPaymentReport());
        Assert.Contains("\"type\":\"payment\"", json);
    }
}
