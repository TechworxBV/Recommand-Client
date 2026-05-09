using System.Collections.ObjectModel;
using System.Text.Json;
using Xunit;

namespace Recommand.Client.Tests;

public class VatPolymorphismTests
{
    [Fact]
    public void Serializing_VatTotals_AsBase_WritesItsFields()
    {
        Vat value = new VatTotals
        {
            TotalVatAmount = "21.00",
            Subtotals = new Collection<VATSubtotal>(),
        };

        var json = JsonSerializer.Serialize(value);

        Assert.Contains("\"totalVatAmount\":\"21.00\"", json);
        Assert.Contains("\"subtotals\":", json);
    }

    [Fact]
    public void Serializing_VatTotalsAutoCalculation_AsBase_WritesItsFields()
    {
        Vat value = new VatTotalsAutoCalculation
        {
            ExemptionReason = "Reverse charge",
        };

        var json = JsonSerializer.Serialize(value);

        Assert.Contains("\"exemptionReason\":\"Reverse charge\"", json);
        Assert.DoesNotContain("\"totalVatAmount\"", json);
    }

    [Fact]
    public void Deserializing_WithTotalVatAmount_GivesVatTotals()
    {
        var json = """{"totalVatAmount":"21.00","subtotals":[]}""";

        var value = JsonSerializer.Deserialize<Vat>(json);

        Assert.IsType<VatTotals>(value);
        Assert.Equal("21.00", ((VatTotals)value!).TotalVatAmount);
    }

    [Fact]
    public void Deserializing_WithoutTotalVatAmount_GivesAutoCalculation()
    {
        var json = """{"exemptionReason":"Reverse charge"}""";

        var value = JsonSerializer.Deserialize<Vat>(json);

        Assert.IsType<VatTotalsAutoCalculation>(value);
        Assert.Equal("Reverse charge", ((VatTotalsAutoCalculation)value!).ExemptionReason);
    }

    [Fact]
    public void Deserializing_EmptyObject_GivesAutoCalculation()
    {
        var value = JsonSerializer.Deserialize<Vat>("{}");

        Assert.IsType<VatTotalsAutoCalculation>(value);
    }

    [Fact]
    public void Deserializing_Null_GivesNull()
    {
        var value = JsonSerializer.Deserialize<Vat>("null");

        Assert.Null(value);
    }

    [Fact]
    public void Serializing_RawVat_Throws()
    {
        var raw = new Vat();

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(raw));
    }

    [Fact]
    public void SendInvoice_VatPropertyAcceptsEitherVariantDirectly()
    {
        var withTotals = new SendInvoice
        {
            Vat = new VatTotals { TotalVatAmount = "21.00", Subtotals = new Collection<VATSubtotal>() },
        };
        Assert.IsType<VatTotals>(withTotals.Vat);

        var withAutoCalc = new SendInvoice
        {
            Vat = new VatTotalsAutoCalculation { ExemptionReason = "n/a" },
        };
        Assert.IsType<VatTotalsAutoCalculation>(withAutoCalc.Vat);
    }
}
