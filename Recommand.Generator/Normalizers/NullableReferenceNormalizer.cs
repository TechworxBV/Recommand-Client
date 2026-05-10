using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Walks every property in every schema recursively and collapses
/// <c>anyOf [X, null]</c> / <c>oneOf [X, null]</c> nullable-reference unions
/// into a plain <c>$ref: X</c>. Lossless except for the explicit nullability
/// annotation (which is preserved at runtime since C# class types are
/// nullable regardless).
///
/// Only collapses when there is exactly one schema reference in the union
/// (modulo any number of null branches). Polymorphic unions with multiple
/// distinct refs are left untouched — those are Pattern A territory.
/// </summary>
internal sealed class NullableReferenceNormalizer : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        var rewrites = 0;

        foreach (var schema in document.Definitions.Values)
            Walk(schema, ref rewrites);

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, op) in pathItem.ActualPathItem)
            {
                if (op.RequestBody?.Content.TryGetValue("application/json", out var rc) == true
                    && rc.Schema is { } reqSchema)
                    Walk(reqSchema, ref rewrites);

                foreach (var (_, response) in op.Responses)
                {
                    if (response.Content.TryGetValue("application/json", out var sc)
                        && sc.Schema is { } respSchema)
                        Walk(respSchema, ref rewrites);
                }
            }
        }

        Console.WriteLine($"Nullable-reference normalizer: collapsed {rewrites} nullable-ref unions to plain $ref.");
    }

    private static void Walk(JsonSchema schema, ref int counter)
    {
        var actual = schema.ActualSchema;
        if (actual.Properties is null) return;

        foreach (var propName in actual.Properties.Keys.ToList())
        {
            var prop = actual.Properties[propName];

            if (TryGetSingleRef(prop, out var target))
            {
                prop.AnyOf.Clear();
                prop.OneOf.Clear();
                prop.Reference = target;
                counter++;
                continue;
            }

            Walk(prop, ref counter);
        }
    }

    private static bool TryGetSingleRef(JsonSchema schema, out JsonSchema target)
    {
        target = null!;
        if (schema.HasReference) return false;

        var union = schema.AnyOf.Concat(schema.OneOf).ToList();
        if (union.Count == 0) return false;

        JsonSchema? singleRef = null;
        foreach (var member in union)
        {
            if (member.Type == JsonObjectType.Null) continue;
            if (!member.HasReference) return false;
            if (singleRef is not null && member.Reference != singleRef) return false;
            singleRef ??= member.Reference;
        }

        if (singleRef is null) return false;
        target = singleRef;
        return true;
    }
}
