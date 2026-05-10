using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Collapses <c>anyOf</c>/<c>oneOf</c> unions whose non-<c>null</c> branches
/// are all the same primitive type (string / number / integer / boolean).
/// These come from specs that express "this string with format X, or this
/// string with a const sentinel, or null" as a union — NJsonSchema can't
/// fit that into a single C# type and falls back to an empty placeholder
/// class (e.g. <c>Email2</c>).
///
/// Collapse rules:
///   - All non-null branches must share a single primitive <c>type</c>.
///   - <c>format</c> is preserved when exactly one distinct format appears
///     across the branches (multiple distinct formats → skip, too lossy).
///   - Nullability is preserved if any branch was <c>type: null</c>.
///   - <c>const</c> sentinels are dropped (e.g. <c>const: ""</c> → just
///     "empty string allowed", which the underlying primitive already permits).
/// </summary>
internal sealed class PrimitiveUnionNormalizer : ISpecNormalizer
{
    private static readonly JsonObjectType[] PrimitiveTypes =
    {
        JsonObjectType.String,
        JsonObjectType.Number,
        JsonObjectType.Integer,
        JsonObjectType.Boolean,
    };

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

        Console.WriteLine($"Primitive-union normalizer: collapsed {rewrites} primitive unions to plain types.");
    }

    private static void Walk(JsonSchema schema, ref int counter)
    {
        var actual = schema.ActualSchema;
        if (actual.Properties is null) return;

        foreach (var propName in actual.Properties.Keys.ToList())
        {
            var prop = actual.Properties[propName];

            if (TryCollapsePrimitiveUnion(prop, out var primitiveType, out var format, out var nullable))
            {
                prop.AnyOf.Clear();
                prop.OneOf.Clear();
                prop.Type = primitiveType;
                if (format is not null) prop.Format = format;
                if (nullable) prop.Type |= JsonObjectType.Null;
                counter++;
                continue;
            }

            Walk(prop, ref counter);
        }
    }

    private static bool TryCollapsePrimitiveUnion(
        JsonSchema schema,
        out JsonObjectType primitiveType,
        out string? format,
        out bool nullable)
    {
        primitiveType = JsonObjectType.None;
        format = null;
        nullable = false;

        if (schema.HasReference) return false;

        var union = schema.AnyOf.Concat(schema.OneOf).ToList();
        if (union.Count == 0) return false;

        JsonObjectType? seenPrimitive = null;
        string? seenFormat = null;
        var hasNull = false;
        var nonNullBranches = 0;

        foreach (var member in union)
        {
            if (member.HasReference) return false;

            if (member.Type == JsonObjectType.Null) { hasNull = true; continue; }

            // Each branch must be exactly one primitive type.
            var memberType = StripNull(member.Type, out var memberHasNull);
            if (memberHasNull) hasNull = true;

            if (!PrimitiveTypes.Contains(memberType)) return false;
            if (seenPrimitive is null) seenPrimitive = memberType;
            else if (seenPrimitive != memberType) return false;

            if (!string.IsNullOrEmpty(member.Format))
            {
                if (seenFormat is null) seenFormat = member.Format;
                else if (seenFormat != member.Format) return false;  // conflicting formats: skip
            }

            nonNullBranches++;
        }

        if (seenPrimitive is null || nonNullBranches < 2) return false;

        primitiveType = seenPrimitive.Value;
        format = seenFormat;
        nullable = hasNull;
        return true;
    }

    private static JsonObjectType StripNull(JsonObjectType type, out bool hadNull)
    {
        hadNull = type.HasFlag(JsonObjectType.Null);
        return type & ~JsonObjectType.Null;
    }
}
