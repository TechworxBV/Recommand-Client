using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// The <c>vat</c> property of the documents you send is
/// <c>anyOf [VatTotals, VatTotalsAutoCalculation, null]</c> with no
/// discriminator. It is retyped to a new, empty <c>Vat</c> definition; the
/// hand-written <c>Polymorphism/Vat.Polymorphism.cs</c> makes both members
/// derive from it and picks between them by shape when reading.
/// </summary>
internal sealed class VatPropertyNormalizer : ISpecNormalizer
{
    private static readonly string[] Members = ["VatTotals", "VatTotalsAutoCalculation"];

    public void Normalize(OpenApiDocument document)
    {
        if (Members.Any(m => !document.Definitions.ContainsKey(m))) return;

        var vat = new JsonSchema
        {
            Type = JsonObjectType.Object,
            Description = "VAT of a document to send: either VatTotals (you provide the totals) or VatTotalsAutoCalculation.",
        };

        var rewrites = 0;
        foreach (var schema in document.Definitions.Values.ToList())
        {
            if (!schema.Properties.TryGetValue("vat", out var property)) continue;
            var union = property.AnyOf.Concat(property.OneOf).Where(m => m.HasReference).Select(m => m.ActualSchema).ToList();
            if (union.Count != Members.Length || Members.Any(m => !union.Contains(document.Definitions[m]))) continue;

            property.AnyOf.Clear();
            property.OneOf.Clear();
            property.Reference = vat;
            rewrites++;
        }

        if (rewrites > 0) document.Definitions["Vat"] = vat;
        Console.WriteLine($"Vat property normalizer: retyped {rewrites} vat properties to Vat.");
    }
}
