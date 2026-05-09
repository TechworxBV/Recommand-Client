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
            }
        }

        Console.WriteLine($"Inline-schema hoister: extracted {hoisted} nested inline objects to definitions.");
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
            if (prop.Properties is null || prop.Properties.Count == 0) continue;
            if (!prop.Type.HasFlag(JsonObjectType.Object) && prop.Type != JsonObjectType.None) continue;

            var pascalProp = char.ToUpperInvariant(propName[0]) + propName.Substring(1);
            var newName = ChooseUniqueDefinitionName(document.Definitions, $"{contextName}{pascalProp}");

            var hoistedSchema = new JsonSchema { Type = JsonObjectType.Object, Title = newName };
            foreach (var (k, v) in prop.Properties.ToList())
                hoistedSchema.Properties.Add(k, v);
            foreach (var r in prop.RequiredProperties.ToList())
                hoistedSchema.RequiredProperties.Add(r);
            if (!string.IsNullOrEmpty(prop.Description))
                hoistedSchema.Description = prop.Description;

            document.Definitions[newName] = hoistedSchema;
            counter++;

            HoistContainer(hoistedSchema, newName, document, ref counter);

            actual.Properties[propName] = new JsonSchemaProperty { Reference = hoistedSchema };
        }
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
}
