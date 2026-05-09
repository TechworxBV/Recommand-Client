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

ISpecNormalizer[] normalizers =
[
    new OperationBodyTitleNormalizer(),
    new SendDocumentPolymorphismNormalizer(),
    new VatPropertyNormalizer(),
    new InlineSchemaHoister(),
];
foreach (var normalizer in normalizers) normalizer.Normalize(document);

await ClientCodeGenerator.GenerateAsync(document, generatedPath);
