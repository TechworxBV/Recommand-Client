using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Generic Pattern A: detects schemas where a discriminator property
/// (string + enum) sits next to a polymorphic property (anyOf/oneOf of refs)
/// and rewrites the parent into an OpenAPI inheritance hierarchy that
/// NSwag emits as typed C# inheritance + JsonInheritanceConverter.
///
/// Detection is generic; per-site naming is supplied via <see cref="Site"/>
/// because the "right" variant name (<c>SendInvoiceRequest</c> vs
/// <c>InvoiceInboxDocument</c> vs whatever) is spec-specific and can't be
/// inferred safely.
/// </summary>
internal sealed class SiblingDiscriminatorPolymorphismNormalizer : ISpecNormalizer
{
    private readonly IReadOnlyList<Site> _sites;

    public SiblingDiscriminatorPolymorphismNormalizer(params Site[] sites)
    {
        _sites = sites;
    }

    public void Normalize(OpenApiDocument document)
    {
        foreach (var site in _sites)
        {
            ApplySite(document, site);
        }
    }

    private static void ApplySite(OpenApiDocument document, Site site)
    {
        PromoteTitledBodyToDefinition(document, site.ParentSchemaName);

        if (!document.Definitions.TryGetValue(site.ParentSchemaName, out var parent))
        {
            return;
        }

        if (parent.DiscriminatorObject is not null && parent.AllOf.Count == 0)
        {
            return;
        }

        if (!parent.ActualProperties.TryGetValue(site.DiscriminatorPropertyName, out var discProp))
        {
            return;
        }
        if (!parent.ActualProperties.TryGetValue(site.PolymorphicPropertyName, out var polyProp))
        {
            return;
        }

        // Dereference: the discriminator property may be a $ref to a hoisted
        // enum schema (the inline-schema hoister extracts inline enums into
        // Definitions). ActualSchema follows the ref.
        var enumValues = discProp.ActualSchema.Enumeration?.Cast<string>().ToList() ?? new();
        if (enumValues.Count == 0) return;

        var fullUnion = polyProp.AnyOf.Concat(polyProp.OneOf).ToList();
        var union = fullUnion.Where(s => s.HasReference).ToList();
        if (union.Count == 0) return;

        // If the polymorphic property includes a null branch, the property is
        // genuinely optional and should not be marked Required on variants.
        var polymorphIsNullable = fullUnion.Any(s => s.Type == JsonObjectType.Null);

        // Strict mode (no RefNameForEnum) requires positional 1:1 match between
        // enum values and union refs. Loose mode (RefNameForEnum supplied)
        // allows extra enum values that have no corresponding ref — those
        // produce variants that inherit the parent without adding a polymorphic
        // payload property.
        if (site.RefNameForEnum is null && enumValues.Count != union.Count) return;

        // Build a name-keyed lookup of the available ref schemas. Used by loose
        // mode; harmless to build in strict mode.
        var refsByName = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in union)
        {
            var refSchema = u.Reference!;
            var refName = document.Definitions.FirstOrDefault(kv => kv.Value == refSchema).Key;
            if (refName is not null) refsByName[refName] = refSchema;
        }

        parent.RequiredProperties.Remove(site.DiscriminatorPropertyName);
        parent.RequiredProperties.Remove(site.PolymorphicPropertyName);
        parent.Properties.Remove(site.DiscriminatorPropertyName);
        parent.Properties.Remove(site.PolymorphicPropertyName);

        parent.DiscriminatorObject = new OpenApiDiscriminator
        {
            PropertyName = site.DiscriminatorPropertyName,
        };

        for (var i = 0; i < enumValues.Count; i++)
        {
            var enumValue = enumValues[i];
            JsonSchema? docSchema;
            string refName;

            if (site.RefNameForEnum is null)
            {
                // Strict positional.
                docSchema = union[i].Reference!;
                refName = document.Definitions.FirstOrDefault(kv => kv.Value == docSchema).Key
                    ?? "Variant" + i;
            }
            else
            {
                refName = site.RefNameForEnum(enumValue);
                refsByName.TryGetValue(refName, out docSchema);
            }

            var variantName = site.VariantNameFor(refName, enumValue);

            var variant = new JsonSchema { Title = variantName };
            variant.AllOf.Add(new JsonSchema { Reference = parent });

            // Always add an addOn — even an empty one. Without it the variant
            // becomes a single-element allOf, which NSwag treats as a parent
            // alias and skips, breaking the JsonInheritanceAttribute mapping.
            var addOn = new JsonSchema { Type = JsonObjectType.Object };
            if (docSchema is not null)
            {
                addOn.Properties.Add(site.PolymorphicPropertyName, new JsonSchemaProperty { Reference = docSchema });
                if (!polymorphIsNullable)
                    addOn.RequiredProperties.Add(site.PolymorphicPropertyName);
            }
            variant.AllOf.Add(addOn);

            document.Definitions[variantName] = variant;
            parent.DiscriminatorObject.Mapping.Add(enumValue, variant);
        }

        Console.WriteLine($"Sibling-discriminator polymorphism @ {site.ParentSchemaName}: rewrote with {enumValues.Count} variants.");
    }

    private static void PromoteTitledBodyToDefinition(OpenApiDocument document, string title)
    {
        if (document.Definitions.ContainsKey(title)) return;

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, op) in pathItem.ActualPathItem)
            {
                if (op.RequestBody?.Content.TryGetValue("application/json", out var rc) == true
                    && rc.Schema is { Title: var t } bodySchema
                    && t == title
                    && bodySchema.Reference is null)
                {
                    document.Definitions[title] = bodySchema;
                    return;
                }

                foreach (var (_, response) in op.Responses)
                {
                    if (response.Content.TryGetValue("application/json", out var sc)
                        && sc.Schema is { Title: var rt } respSchema
                        && rt == title
                        && respSchema.Reference is null)
                    {
                        document.Definitions[title] = respSchema;
                        return;
                    }
                }
            }
        }
    }

    /// <param name="RefNameForEnum">
    /// Optional. Maps an enum value to the expected $ref schema name. When supplied,
    /// loose matching mode is enabled: enum values whose mapped ref name isn't found
    /// in the union produce variants that inherit the parent but carry no polymorphic
    /// payload property. When null, strict positional matching is used and the count
    /// of enum values and union refs must be equal.
    /// </param>
    public sealed record Site(
        string ParentSchemaName,
        string DiscriminatorPropertyName,
        string PolymorphicPropertyName,
        Func<string, string, string> VariantNameFor,
        Func<string, string>? RefNameForEnum = null);
}
