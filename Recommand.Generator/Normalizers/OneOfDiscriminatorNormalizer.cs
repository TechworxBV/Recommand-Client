using System.Runtime.CompilerServices;
using System.Text;
using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Rewrites OpenAPI 3.1 polymorphism (a definition that is
/// <c>oneOf: [refs]</c> plus a <c>discriminator</c>) into the <c>allOf</c>
/// inheritance shape NSwag emits as a C# class hierarchy.
///
/// Properties every variant declares identically move to the parent; the
/// rest stay on the variant. The discriminator property is dropped from
/// both: the converter reads and writes it on the wire, and the C# type
/// already says which variant it is.
/// </summary>
internal sealed class OneOfDiscriminatorNormalizer : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        var owners = new Dictionary<JsonSchema, string>(ReferenceEqualityComparer.Instance);

        foreach (var (parentName, parent) in document.Definitions.ToList())
        {
            if (parent.DiscriminatorObject is not { PropertyName.Length: > 0 } discriminator) continue;
            if (parent.OneOf.Count == 0 || parent.OneOf.Any(m => !m.HasReference)) continue;

            var variants = parent.OneOf.Select(m => m.ActualSchema).ToList();
            foreach (var variant in variants)
            {
                if (owners.TryGetValue(variant, out var owner))
                {
                    var name = document.Definitions.First(kv => ReferenceEquals(kv.Value, variant)).Key;
                    throw new InvalidOperationException(
                        $"'{name}' is a variant of both '{owner}' and '{parentName}'. A C# class has one base type: " +
                        "give one union its own copy with SpecSource.CloneVariantForUnion in Program.cs.");
                }
                owners[variant] = parentName;
            }

            var property = discriminator.PropertyName;
            var common = variants[0].Properties
                .Where(kv => kv.Key != property
                             && variants.All(v => v.Properties.TryGetValue(kv.Key, out var other)
                                                  && Fingerprint(kv.Value) == Fingerprint(other)))
                .ToList();

            parent.Type = JsonObjectType.Object;
            foreach (var (name, _) in common)
            {
                var required = variants.All(v => v.RequiredProperties.Contains(name));
                var schema = variants[0].Properties[name];
                foreach (var variant in variants)
                {
                    variant.Properties.Remove(name);
                    variant.RequiredProperties.Remove(name);
                }
                parent.Properties.Add(name, schema);
                if (required) parent.RequiredProperties.Add(name);
            }

            foreach (var variant in variants)
            {
                var own = new JsonSchema { Type = JsonObjectType.Object };
                foreach (var (name, schema) in variant.Properties.ToList())
                {
                    variant.Properties.Remove(name);
                    if (name != property) own.Properties.Add(name, schema);
                }
                foreach (var name in variant.RequiredProperties.Where(n => n != property)) own.RequiredProperties.Add(name);
                variant.RequiredProperties.Clear();

                // Always two members: a single-element allOf is treated by
                // NSwag as an alias of the parent rather than a subclass.
                variant.AllOf.Add(new JsonSchema { Reference = parent });
                variant.AllOf.Add(own);
            }

            parent.OneOf.Clear();
            Console.WriteLine($"OneOf-discriminator @ {parentName}: {variants.Count} variants, {common.Count} shared properties.");
        }
    }

    private static string Fingerprint(JsonSchema schema)
    {
        var sb = new StringBuilder();
        Emit(schema, sb);
        return sb.ToString();
    }

    private static void Emit(JsonSchema schema, StringBuilder sb)
    {
        if (schema.HasReference)
        {
            sb.Append("ref:").Append(RuntimeHelpers.GetHashCode(schema.ActualSchema)).Append(';');
            return;
        }
        sb.Append("type:").Append((int)schema.Type).Append(";format:").Append(schema.Format).Append(';');
        if (schema.Enumeration.Count > 0)
            sb.Append("enum:").AppendJoin(',', schema.Enumeration.Select(v => v?.ToString()).Order(StringComparer.Ordinal)).Append(';');
        if (InlineOneOfBodyNormalizer.ConstOf(schema) is { } constant) sb.Append("const:").Append(constant).Append(';');
        if (schema.Item is { } item) { sb.Append("items:"); Emit(item, sb); }
        foreach (var (name, property) in schema.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(name).Append('{');
            Emit(property, sb);
            sb.Append('}');
        }
        foreach (var member in schema.AnyOf.Concat(schema.OneOf).Concat(schema.AllOf))
        {
            sb.Append('[');
            Emit(member, sb);
            sb.Append(']');
        }
    }
}
