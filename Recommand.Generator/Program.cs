using NSwag;
using Recommand.Generator;
using Recommand.Generator.Normalizers;

// Usage:
//   dotnet run --project Recommand.Generator            regenerate from spec/openapi.json
//   dotnet run --project Recommand.Generator -- --fetch download the live spec into spec/openapi.json first
//
// spec/openapi.json is an unmodified snapshot of the upstream spec. Anything
// the SDK needs that upstream does not publish lives in a supplement file
// (spec/webhooks.json) and is merged in here.
var repoRoot = FindRepoRoot();
var specPath = Path.Combine(repoRoot, "spec", "openapi.json");
var outputDirectory = Path.Combine(repoRoot, "Recommand.Client", "Generated");

if (args.Contains("--fetch")) await SpecSource.RefreshSnapshotAsync(specPath);

var spec = await SpecSource.LoadAsync(specPath, Path.Combine(repoRoot, "spec", "webhooks.json"));

// FrenchB2BiPaymentReport is a variant of both the report you submit
// (FrenchB2BiReport) and the report you read back (StoredFrenchB2BiReport).
SpecSource.CloneVariantForUnion(spec,
    union: "StoredFrenchB2BiReport",
    variant: "FrenchB2BiPaymentReport",
    cloneName: "StoredFrenchB2BiPaymentReport");

var document = await OpenApiDocument.FromJsonAsync(spec.ToJsonString());
Console.WriteLine($"Loaded: {document.Info.Title} ({document.Paths.Count} paths, {document.Definitions.Count} schemas)");

// DocumentType value → schema of the document's `parsed` payload.
var parsedPayloads = new Dictionary<string, string?>
{
    ["invoice"] = "Invoice",
    ["creditNote"] = "CreditNote",
    ["selfBillingInvoice"] = "SelfBillingInvoice",
    ["selfBillingCreditNote"] = "SelfBillingCreditNote",
    ["messageLevelResponse"] = "MessageLevelResponse",
    ["frenchInvoicingCdar"] = "FranceCdar",
    ["frenchB2CSalesReport"] = "FrenchB2CSalesReport",
    ["frenchB2CPaymentReport"] = "FrenchB2CPaymentsReport",
    ["frenchB2BiInvoiceReport"] = "StoredFrenchB2BiInvoiceReport",
    ["frenchB2BiPaymentReport"] = "StoredFrenchB2BiPaymentReport",
    ["unknown"] = null,
};

// Order matters: each pass simplifies the document for the ones after it.
ISpecNormalizer[] normalizers =
[
    new OperationBodyTitleNormalizer(),   // name anonymous request/response bodies after their operation
    new NullableReferenceNormalizer(),    // oneOf [X, null] → $ref X
    new PrimitiveUnionNormalizer(),       // anyOf of one primitive type → that type
    new VatPropertyNormalizer(),          // undiscriminated VAT union → Vat base
    new InlineOneOfBodyNormalizer(new Dictionary<string, string>
    {
        ["sendDocument"] = "Send",
        ["generate"] = "Generate",
    }),                                   // inline oneOf request bodies → named union with discriminator
    new InlineSchemaHoister(),            // nested inline objects/enums → named definitions
    new OneOfDiscriminatorNormalizer(),   // oneOf + discriminator → inheritance
    new SiblingDiscriminatorNormalizer(
        new("TransmittedDocument", "type", "parsed", parsedPayloads),
        new("TransmittedDocumentSummary", "type", "parsed", parsedPayloads)),
    new UnknownVariantNormalizer(),       // Unknown{Base} fallback per hierarchy
    new MixedContentResponseNormalizer(), // text-or-binary responses → stream
];
foreach (var normalizer in normalizers) normalizer.Normalize(document);

await ClientCodeGenerator.GenerateAsync(document, outputDirectory);

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "recommand-client.sln"))) return dir.FullName;
    }
    throw new InvalidOperationException("Could not find recommand-client.sln above " + AppContext.BaseDirectory);
}
