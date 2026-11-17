using NJsonSchema;
using NSwag;
using Recommand.Generator.Naming;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Handles a discriminator and its payload sitting next to each other as two
/// properties of one object: an enum property (<c>type</c>) and a property
/// that is a union of refs (<c>parsed</c>), where the enum value says which
/// member of the union the payload is. The object becomes a polymorphic
/// base with one subtype per enum value, each carrying the payload property
/// typed as its own schema.
///
/// The spec does not say which enum value goes with which union member, so
/// each <see cref="Site"/> supplies that mapping. An enum value mapped to
/// <c>null</c> has no payload and gets no subtype: it deserialises to the
/// hierarchy's <c>Unknown{Base}</c> fallback. The run fails when
/// the spec has an enum value the mapping does not mention, so a new
/// document type is noticed rather than silently left untyped.
/// </summary>
internal sealed class SiblingDiscriminatorNormalizer(params SiblingDiscriminatorNormalizer.Site[] sites) : ISpecNormalizer
{
    public sealed record Site(
        string Parent,
        string DiscriminatorProperty,
        string PayloadProperty,
        IReadOnlyDictionary<string, string?> PayloadSchemaFor);

    public void Normalize(OpenApiDocument document)
    {
        foreach (var site in sites)
        {
            var parent = document.Definitions[site.Parent];
            var values = parent.Properties[site.DiscriminatorProperty].ActualSchema.Enumeration.Cast<string>().ToList();
            var payload = parent.Properties[site.PayloadProperty];
            var nullable = payload.AnyOf.Concat(payload.OneOf).Any(m => m.Type == JsonObjectType.Null);

            var unmapped = values.Where(v => !site.PayloadSchemaFor.ContainsKey(v)).ToList();
            if (unmapped.Count > 0)
                throw new InvalidOperationException(
                    $"{site.Parent}.{site.DiscriminatorProperty} has values with no payload mapping in Program.cs: " +
                    string.Join(", ", unmapped));

            foreach (var name in new[] { site.DiscriminatorProperty, site.PayloadProperty })
            {
                parent.Properties.Remove(name);
                parent.RequiredProperties.Remove(name);
            }
            parent.DiscriminatorObject = new OpenApiDiscriminator { PropertyName = site.DiscriminatorProperty };

            foreach (var value in values)
            {
                if (site.PayloadSchemaFor[value] is not { } schemaName) continue;

                var own = new JsonSchema { Type = JsonObjectType.Object };
                own.Properties.Add(site.PayloadProperty, new JsonSchemaProperty
                {
                    Reference = document.Definitions[schemaName],
                    Description = payload.Description,
                });
                if (!nullable) own.RequiredProperties.Add(site.PayloadProperty);

                var variantName = site.Parent + Names.Pascal(value);
                var variant = new JsonSchema { Title = variantName };
                variant.AllOf.Add(new JsonSchema { Reference = parent });
                variant.AllOf.Add(own);
                document.Definitions[variantName] = variant;
                parent.DiscriminatorObject.Mapping.Add(value, variant);
            }

            Console.WriteLine($"Sibling discriminator @ {site.Parent}: {parent.DiscriminatorObject.Mapping.Count} variants on '{site.DiscriminatorProperty}'.");
        }
    }
}
