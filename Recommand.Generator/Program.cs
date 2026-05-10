using NSwag;
using Recommand.Generator;
using Recommand.Generator.Normalizers;

// Spec source is the in-repo file. No URL fetch — see commit history for
// when this changed. To update the spec, replace spec/openapi.json directly.
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var generatedPath = Path.Combine(repoRoot, "Recommand.Client", "Generated", "RecommandClient.g.cs");
var specPath = Path.Combine(repoRoot, "spec", "openapi.json");

Console.WriteLine($"Reading spec from {specPath}");
var document = await OpenApiDocument.FromFileAsync(specPath);
Console.WriteLine($"Loaded: {document.Info.Title} v{document.Info.Version} ({document.Paths.Count} paths)");

static string PascalCase(string s) =>
    string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

ISpecNormalizer[] normalizers =
[
    new OperationBodyTitleNormalizer(),
    new NullableReferenceNormalizer(),
    new PrimitiveUnionNormalizer(),
    new VatPropertyNormalizer(),
    new InlineSchemaHoister(),
    // Modern OAS 3.1 oneOf+discriminator → allOf inheritance. Generic;
    // walks all definitions and finds the pattern automatically.
    new OneOfDiscriminatorNormalizer(),
    // Polymorphism rewrite needs the hoister's parents (Get*Document) to exist
    // first; SendDocumentRequest's parent is promoted in-normalizer regardless.
    new SiblingDiscriminatorPolymorphismNormalizer(
        new SiblingDiscriminatorPolymorphismNormalizer.Site(
            ParentSchemaName: "SendDocumentRequest",
            DiscriminatorPropertyName: "documentType",
            PolymorphicPropertyName: "document",
            VariantNameFor: (refName, disc) =>
                refName.StartsWith("Send")
                    ? refName + "Request"
                    : "Send" + PascalCase(disc) + "Request"),
        new SiblingDiscriminatorPolymorphismNormalizer.Site(
            ParentSchemaName: "GetDocumentResponseDocument",
            DiscriminatorPropertyName: "type",
            PolymorphicPropertyName: "parsed",
            VariantNameFor: (_, disc) => "GetDocumentResponseDocument" + PascalCase(disc),
            RefNameForEnum: PascalCase),
        new SiblingDiscriminatorPolymorphismNormalizer.Site(
            ParentSchemaName: "GetDocumentsResponseDocument",
            DiscriminatorPropertyName: "type",
            PolymorphicPropertyName: "parsed",
            VariantNameFor: (_, disc) => "GetDocumentsResponseDocument" + PascalCase(disc),
            RefNameForEnum: PascalCase)),
    new StructuralDeduplicator(
        new StructuralDeduplicator.NamingRule(
            Match: schema =>
                schema.ActualProperties.Count == 2
                && schema.ActualProperties.TryGetValue("success", out var s)
                && s.Type.HasFlag(NJsonSchema.JsonObjectType.Boolean)
                && schema.ActualProperties.TryGetValue("errors", out var e)
                && e.Type.HasFlag(NJsonSchema.JsonObjectType.Object)
                && e.AdditionalPropertiesSchema is { } addl
                && addl.Type.HasFlag(NJsonSchema.JsonObjectType.Array),
            Name: "ValidationErrorResponse")),
];
foreach (var normalizer in normalizers) normalizer.Normalize(document);

await ClientCodeGenerator.GenerateAsync(document, generatedPath);
