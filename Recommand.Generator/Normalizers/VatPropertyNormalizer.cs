using NSwag;

namespace Recommand.Generator.Normalizers;

internal sealed class VatPropertyNormalizer : ISpecNormalizer
{
    private static readonly string[] DocumentSchemas =
    [
        "Invoice", "CreditNote", "SelfBillingInvoice", "SelfBillingCreditNote",
        "SendInvoice", "SendCreditNote", "SendSelfBillingInvoice", "SendSelfBillingCreditNote",
    ];

    public void Normalize(OpenApiDocument document)
    {
        if (!document.Definitions.TryGetValue("VatTotals", out var vatTotals)) return;

        var rewrites = 0;
        foreach (var name in DocumentSchemas)
        {
            if (!document.Definitions.TryGetValue(name, out var schema)) continue;
            if (!schema.ActualProperties.TryGetValue("vat", out var vatProp)) continue;

            var union = vatProp.AnyOf.Concat(vatProp.OneOf).ToList();
            if (union.Count == 0) continue;
            if (!union.Any(s => s.HasReference && s.Reference == vatTotals)) continue;

            vatProp.AnyOf.Clear();
            vatProp.OneOf.Clear();
            vatProp.Reference = vatTotals;
            rewrites++;
        }

        Console.WriteLine($"Vat property normalizer: rewrote {rewrites} vat properties to plain $ref to VatTotals.");
    }
}
