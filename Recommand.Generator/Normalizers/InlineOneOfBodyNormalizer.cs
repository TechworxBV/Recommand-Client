using NJsonSchema;
using NSwag;
using Recommand.Generator.Naming;

namespace Recommand.Generator.Normalizers;

/// <summary>
/// Request bodies written as an inline <c>oneOf</c> of inline objects that
/// all carry the same <c>const</c>-valued property (a discriminator in all
/// but name) become a named parent definition with named variants and an
/// explicit discriminator. <see cref="OneOfDiscriminatorNormalizer"/> then
/// turns that into an inheritance hierarchy.
/// </summary>
/// <param name="stems">
/// Operation id → name stem. Stem <c>Send</c> names the parent
/// <c>SendDocumentRequest</c> and the variants <c>Send{Discriminator}Request</c>.
/// Operations without an entry use their Pascal-cased operation id as stem.
/// </param>
internal sealed class InlineOneOfBodyNormalizer(IReadOnlyDictionary<string, string> stems) : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, operation) in pathItem.ActualPathItem)
            {
                if (operation.RequestBody?.Content.TryGetValue("application/json", out var content) != true || content is null) continue;
                if (content.Schema is not { HasReference: false } body) continue;
                if (body.OneOf.Count < 2 || body.OneOf.Any(v => v.HasReference)) continue;

                var discriminator = FindSharedConstProperty(body.OneOf);
                if (discriminator is null) continue;

                var stem = stems.TryGetValue(operation.OperationId, out var s) ? s : Names.Pascal(operation.OperationId);
                var parentName = stem + "DocumentRequest";

                var parent = new JsonSchema
                {
                    Description = body.Description,
                    DiscriminatorObject = new OpenApiDiscriminator { PropertyName = discriminator },
                };

                foreach (var variant in body.OneOf.ToList())
                {
                    var value = ConstOf(variant.Properties[discriminator])!;
                    var variantName = $"{stem}{Names.Pascal(value)}Request";
                    variant.Title = variantName;
                    document.Definitions[variantName] = variant;
                    parent.OneOf.Add(new JsonSchema { Reference = variant });
                    parent.DiscriminatorObject.Mapping[value] = variant;
                }

                document.Definitions[parentName] = parent;
                content.Schema = new JsonSchema { Reference = parent };
                Console.WriteLine($"Inline oneOf body @ {operation.OperationId}: {parentName} with {parent.OneOf.Count} variants on '{discriminator}'.");
            }
        }
    }

    private static string? FindSharedConstProperty(ICollection<JsonSchema> variants) =>
        variants.First().Properties.Keys.FirstOrDefault(name =>
            variants.All(v => v.Properties.TryGetValue(name, out var p) && ConstOf(p) is not null));

    internal static string? ConstOf(JsonSchema schema) =>
        schema.ExtensionData is { } data && data.TryGetValue("const", out var value) ? value?.ToString() : null;
}
