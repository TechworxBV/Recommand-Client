using NSwag;
using Recommand.Generator;
using Recommand.Generator.Normalizers;

const string SpecUrl = "https://peppol.recommand.eu/openapi";

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var generatedPath = Path.Combine(repoRoot, "Recommand.Client", "Generated", "RecommandClient.g.cs");
var snapshotPath = Path.Combine(repoRoot, "spec", "openapi.json");
var localOverride = Environment.GetEnvironmentVariable("RECOMMAND_SPEC_PATH");

var rawJson = await SpecFetcher.FetchAsync(SpecUrl, localOverride);
await SpecSnapshot.WriteAsync(rawJson, snapshotPath);

var document = await OpenApiDocument.FromJsonAsync(rawJson);
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
