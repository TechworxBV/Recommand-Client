using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Adds an <c>Unknown{Base}</c> subtype to every polymorphic hierarchy. It is
/// not in the discriminator mapping; the SDK's
/// <c>JsonInheritanceConverter</c> deserialises to it when the API sends a
/// discriminator value this SDK version does not know, instead of throwing.
/// </summary>
internal sealed class UnknownVariantNormalizer : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        var added = 0;
        foreach (var (name, schema) in document.Definitions.ToList())
        {
            if (schema.DiscriminatorObject is null) continue;

            var unknown = new JsonSchema
            {
                Description = $"A {name} whose discriminator this SDK version does not recognise. " +
                              "Everything it carried beyond the shared properties is in AdditionalProperties.",
            };
            unknown.AllOf.Add(new JsonSchema { Reference = schema });
            unknown.AllOf.Add(new JsonSchema { Type = JsonObjectType.Object });
            document.Definitions["Unknown" + name] = unknown;
            added++;
        }
        Console.WriteLine($"Unknown-variant normalizer: added {added} fallback subtypes.");
    }
}
