using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Rewrites OpenAPI 3.1-style polymorphism (a parent with
/// <c>oneOf: [refs]</c> and a <c>discriminator</c>) into the
/// <c>allOf</c> inheritance shape NSwag emits as a typed C# hierarchy
/// with <see cref="JsonInheritanceConverter{TBase}"/> dispatch.
///
/// Detects sites generically: any definition with a non-null
/// <see cref="JsonSchema.DiscriminatorObject"/> and ≥2 referenced
/// <c>OneOf</c> members. Property intersection across variants is
/// promoted to the parent; remaining per-variant properties go into a
/// fresh <c>allOf:[{$ref: parent}, addOn]</c> hierarchy. The
/// discriminator property itself is removed from the parent (NSwag's
/// <see cref="JsonInheritanceConverter{TBase}"/> reads the JSON wire
/// field directly, not via a C# property).
///
/// This is the structural sibling of
/// <see cref="SiblingDiscriminatorPolymorphismNormalizer"/> for the
/// modern OpenAPI 3.1 polymorphism shape:
///   - Sibling-discriminator: discriminator + polymorphic-payload are
///     two adjacent properties on the parent (Pattern A).
///   - OneOf+discriminator: parent is a pure union (no own properties),
///     each variant is a complete schema (Pattern B / OAS 3.1 standard).
/// </summary>
internal sealed class OneOfDiscriminatorNormalizer : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        var rewrites = 0;

        foreach (var (parentName, parent) in document.Definitions.ToList())
        {
            if (TryRewriteSite(parent, parentName)) rewrites++;
        }

        Console.WriteLine($"OneOf-discriminator normalizer: rewrote {rewrites} polymorphism sites.");
    }

    private static bool TryRewriteSite(JsonSchema parent, string parentName)
    {
        if (parent.DiscriminatorObject is not { } discriminator) return false;
        if (string.IsNullOrEmpty(discriminator.PropertyName)) return false;
        if (parent.OneOf.Count < 2) return false;

        var variants = parent.OneOf
            .Select(m => m.HasReference ? m.Reference : null)
            .Where(s => s is not null)
            .Cast<JsonSchema>()
            .ToList();
        if (variants.Count != parent.OneOf.Count) return false;  // mixed inline + ref → bail

        // Already-rewritten? If every variant is already in allOf:[parent, …]
        // shape, skip — idempotent.
        if (variants.All(v => v.AllOf.Any(a => ReferenceEquals(a.Reference, parent))))
            return false;

        var commonProperties = ComputeCommonProperties(variants, discriminator.PropertyName);

        // Promote: the parent becomes a real object schema.
        parent.Type = JsonObjectType.Object;
        foreach (var (name, schema) in commonProperties)
        {
            // Don't override if parent already has the property (defensive;
            // shouldn't happen for OneOf-with-empty-parent but is safe).
            if (!parent.Properties.ContainsKey(name)) parent.Properties.Add(name, schema);
        }

        // Required on parent: union of properties every variant required
        // (and that we promoted).
        foreach (var name in commonProperties.Keys)
        {
            if (variants.All(v => v.RequiredProperties.Contains(name))
                && !parent.RequiredProperties.Contains(name))
            {
                parent.RequiredProperties.Add(name);
            }
        }

        // Strip the discriminator property from the parent. NSwag's
        // JsonInheritanceConverter handles the wire-format dispatch based on
        // the JSON field directly; emitting the discriminator as a C# property
        // here would be redundant (and NSwag would strip it during code-gen
        // anyway, same as SendDocumentRequest's `documentType`). Consumers can
        // read the wire value via AdditionalProperties when working with the
        // base type for unknown future event types — see the partial class.
        parent.Properties.Remove(discriminator.PropertyName);
        parent.RequiredProperties.Remove(discriminator.PropertyName);

        // Convert each variant: clear its own properties, install
        // allOf:[{$ref: parent}, addOn-with-remaining].
        foreach (var variant in variants)
        {
            var addOn = new JsonSchema { Type = JsonObjectType.Object };
            foreach (var (propName, propSchema) in variant.Properties.ToList())
            {
                if (commonProperties.ContainsKey(propName)) continue;
                if (propName == discriminator.PropertyName) continue;
                addOn.Properties.Add(propName, propSchema);
            }
            foreach (var req in variant.RequiredProperties.ToList())
            {
                if (commonProperties.ContainsKey(req)) continue;
                if (req == discriminator.PropertyName) continue;
                addOn.RequiredProperties.Add(req);
            }

            variant.Properties.Clear();
            variant.RequiredProperties.Clear();
            // Keep variant's existing AllOf entries, prepend the parent ref.
            // (In practice variants in this pattern have no AllOf yet.)
            var existingAllOf = variant.AllOf.ToList();
            variant.AllOf.Clear();
            variant.AllOf.Add(new JsonSchema { Reference = parent });
            variant.AllOf.Add(addOn);
            foreach (var sub in existingAllOf) variant.AllOf.Add(sub);
        }

        // Parent's oneOf is the input; clear it now that we've moved the
        // structure into allOf-inheritance form.
        parent.OneOf.Clear();

        Console.WriteLine($"OneOf-discriminator @ {parentName}: rewrote with {variants.Count} variants " +
                          $"(promoted {commonProperties.Count} common props).");
        return true;
    }

    /// <summary>
    /// Properties that appear in every variant with a matching structural
    /// fingerprint. The discriminator property is matched by name+base-type
    /// only (ignoring its <c>const</c> value, which differs per variant by
    /// design) — but it's included in this dictionary for caller bookkeeping;
    /// the rewriter strips it from the parent regardless.
    /// </summary>
    private static Dictionary<string, JsonSchemaProperty> ComputeCommonProperties(
        List<JsonSchema> variants,
        string discriminatorPropertyName)
    {
        var common = new Dictionary<string, JsonSchemaProperty>(StringComparer.Ordinal);
        if (variants.Count == 0) return common;

        var first = variants[0];
        if (first.Properties is null) return common;

        foreach (var (name, prop) in first.Properties)
        {
            var fpFirst = name == discriminatorPropertyName
                ? FingerprintTypeOnly(prop)
                : FingerprintProperty(prop);

            var allMatch = variants.All(v =>
            {
                if (v.Properties is null || !v.Properties.TryGetValue(name, out var other)) return false;
                var fpOther = name == discriminatorPropertyName
                    ? FingerprintTypeOnly(other)
                    : FingerprintProperty(other);
                return fpFirst == fpOther;
            });

            if (allMatch) common[name] = prop;
        }

        return common;
    }

    // ---------- per-property fingerprint ----------
    //
    // Lighter than StructuralDeduplicator's full schema fingerprint —
    // we only need to tell "same property shape" vs "different property
    // shape" within the variants of a single polymorphism site.

    private static string FingerprintProperty(JsonSchema p)
    {
        var sb = new System.Text.StringBuilder();
        Emit(p, sb);
        return sb.ToString();
    }

    private static string FingerprintTypeOnly(JsonSchema p)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("type:").Append((int)p.Type).Append(';');
        if (!string.IsNullOrEmpty(p.Format)) sb.Append("format:").Append(p.Format).Append(';');
        return sb.ToString();
    }

    private static void Emit(JsonSchema schema, System.Text.StringBuilder sb)
    {
        if (schema.HasReference)
        {
            sb.Append("ref:").Append(schema.Reference!.GetHashCode()).Append(';');
            return;
        }
        sb.Append("type:").Append((int)schema.Type).Append(';');
        if (!string.IsNullOrEmpty(schema.Format)) sb.Append("format:").Append(schema.Format).Append(';');

        if (schema.Enumeration is { Count: > 0 } enumValues)
        {
            sb.Append("enum:[");
            foreach (var v in enumValues.Select(x => x?.ToString() ?? "null").OrderBy(x => x, StringComparer.Ordinal))
                sb.Append(v).Append(',');
            sb.Append("];");
        }
        if (schema.ExtensionData is { } ext && ext.TryGetValue("const", out var c) && c is not null)
        {
            sb.Append("const:").Append(c).Append(';');
        }
        if (schema.Item is not null)
        {
            sb.Append("items:");
            Emit(schema.Item, sb);
        }
    }
}
