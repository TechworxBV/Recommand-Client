using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

internal sealed class InlineSchemaHoister : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        var hoisted = 0;

        foreach (var name in document.Definitions.Keys.ToList())
        {
            if (document.Definitions.TryGetValue(name, out var schema))
                HoistContainer(schema, name, document, ref hoisted);
        }

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, operation) in pathItem.ActualPathItem)
            {
                if (operation.RequestBody?.Content.TryGetValue("application/json", out var rc) == true
                    && rc.Schema is { Title: { Length: > 0 } reqTitle } reqSchema)
                {
                    HoistContainer(reqSchema, reqTitle, document, ref hoisted);
                }
                foreach (var (_, response) in operation.Responses)
                {
                    if (response.Content.TryGetValue("application/json", out var sc)
                        && sc.Schema is { Title: { Length: > 0 } respTitle } respSchema)
                    {
                        HoistContainer(respSchema, respTitle, document, ref hoisted);
                    }
                }

                // Hoist inline enums on operation parameters (path/query/header).
                // Without this, NSwag falls back to generating bare enum types named
                // after the parameter (e.g. "Direction", "Type") which collide and
                // give no caller context.
                if (string.IsNullOrEmpty(operation.OperationId)) continue;
                var pascalOp = char.ToUpperInvariant(operation.OperationId[0]) + operation.OperationId.Substring(1);
                foreach (var param in operation.Parameters)
                {
                    if (param.Schema is { Reference: null } pSchema
                        && pSchema.Enumeration is { Count: > 0 }
                        && string.IsNullOrEmpty(param.Name) == false)
                    {
                        var pascalParam = char.ToUpperInvariant(param.Name[0]) + param.Name.Substring(1);
                        var name = ChooseUniqueDefinitionName(document.Definitions, $"{pascalOp}{pascalParam}");
                        var promoted = HoistEnum(pSchema, name, document, ref hoisted);
                        param.Schema = new JsonSchema { Reference = promoted };
                    }
                }
            }
        }

        Console.WriteLine($"Inline-schema hoister: extracted {hoisted} nested inline objects/enums to definitions.");
    }

    private static void HoistContainer(JsonSchema container, string contextName, OpenApiDocument document, ref int counter)
    {
        var actual = container.ActualSchema;
        var props = actual.Properties;
        if (props is null) return;

        foreach (var propName in props.Keys.ToList())
        {
            var prop = props[propName];
            if (prop.Reference is not null) continue;

            var pascalProp = char.ToUpperInvariant(propName[0]) + propName.Substring(1);

            // Inline enum property: hoist as a definition so NSwag emits a
            // contextually-named enum type instead of a bare {PropName} (which
            // collides across operations).
            if (prop.Enumeration is { Count: > 0 }
                && (prop.Type.HasFlag(JsonObjectType.String) || prop.Type.HasFlag(JsonObjectType.Integer)))
            {
                var name = ChooseUniqueDefinitionName(document.Definitions, $"{contextName}{pascalProp}");
                var promoted = HoistEnum(prop, name, document, ref counter);
                actual.Properties[propName] = new JsonSchemaProperty { Reference = promoted };
                continue;
            }

            // Inline object property: hoist the property itself.
            if (prop.Properties is { Count: > 0 }
                && (prop.Type.HasFlag(JsonObjectType.Object) || prop.Type == JsonObjectType.None))
            {
                var name = ChooseUniqueDefinitionName(document.Definitions, $"{contextName}{pascalProp}");
                var promoted = HoistObject(prop, name, document, ref counter);
                actual.Properties[propName] = new JsonSchemaProperty { Reference = promoted };
                HoistContainer(promoted, name, document, ref counter);
                continue;
            }

            // Array property with inline-object items: hoist the items schema.
            if (prop.Type.HasFlag(JsonObjectType.Array)
                && prop.Item is { Reference: null, Properties: { Count: > 0 } } itemSchema
                && (itemSchema.Type.HasFlag(JsonObjectType.Object) || itemSchema.Type == JsonObjectType.None))
            {
                var itemPascal = Singularize(pascalProp);
                var name = ChooseUniqueDefinitionName(document.Definitions, $"{contextName}{itemPascal}");
                var promoted = HoistObject(itemSchema, name, document, ref counter);
                prop.Item = new JsonSchema { Reference = promoted };
                HoistContainer(promoted, name, document, ref counter);
            }
        }
    }

    private static JsonSchema HoistObject(JsonSchema source, string name, OpenApiDocument document, ref int counter)
    {
        var hoisted = new JsonSchema { Type = JsonObjectType.Object, Title = name };
        foreach (var (k, v) in source.Properties.ToList())
            hoisted.Properties.Add(k, v);
        foreach (var r in source.RequiredProperties.ToList())
            hoisted.RequiredProperties.Add(r);
        if (!string.IsNullOrEmpty(source.Description))
            hoisted.Description = source.Description;
        document.Definitions[name] = hoisted;
        counter++;
        return hoisted;
    }

    private static JsonSchema HoistEnum(JsonSchema source, string name, OpenApiDocument document, ref int counter)
    {
        // Strip the null flag from the type when hoisting — nullability is a
        // property-level concern, not an enum definition concern.
        var type = source.Type & ~JsonObjectType.Null;
        var hoisted = new JsonSchema { Type = type, Title = name };
        if (!string.IsNullOrEmpty(source.Format)) hoisted.Format = source.Format;
        foreach (var v in source.Enumeration.ToList()) hoisted.Enumeration.Add(v);
        foreach (var n in source.EnumerationNames.ToList()) hoisted.EnumerationNames.Add(n);
        if (!string.IsNullOrEmpty(source.Description)) hoisted.Description = source.Description;
        document.Definitions[name] = hoisted;
        counter++;
        return hoisted;
    }

    private static string ChooseUniqueDefinitionName(IDictionary<string, JsonSchema> definitions, string preferred)
    {
        if (!definitions.ContainsKey(preferred)) return preferred;
        for (var i = 2; ; i++)
        {
            var candidate = preferred + i;
            if (!definitions.ContainsKey(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Naive English singularization for hoisted item names. Good enough for
    /// API property naming — handles -ies/-ses/-s. The structural deduplicator
    /// catches identical shapes regardless of name, so a missed singularization
    /// just means a slightly awkward type name, not a duplicate type.
    /// </summary>
    private static string Singularize(string name)
    {
        if (name.Length > 3 && name.EndsWith("ies", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 3) + "y";
        if (name.Length > 3 && name.EndsWith("ses", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 2);
        if (name.Length > 2
            && name.EndsWith("s", StringComparison.Ordinal)
            && !name.EndsWith("ss", StringComparison.Ordinal)
            && !name.EndsWith("us", StringComparison.Ordinal)
            && !name.EndsWith("is", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 1);
        return name;
    }
}
