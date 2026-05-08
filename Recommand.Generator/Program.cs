using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using NJsonSchema.CodeGeneration.CSharp;
using NSwag;
using NSwag.CodeGeneration.CSharp;
using NSwag.CodeGeneration.OperationNameGenerators;

const string SpecUrl = "https://peppol.recommand.eu/openapi";

// Resolve the repo root relative to this generator's build output so
// `dotnet run --project Recommand.Generator` works from any cwd.
//
// AppContext.BaseDirectory ends in: Recommand.Generator/bin/<Config>/<Tfm>/
// Walk up four levels to the repo root.
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

var generatedDir = Path.Combine(repoRoot, "Recommand.Client", "Generated");
var generatedPath = Path.Combine(generatedDir, "RecommandClient.g.cs");
var specPath = Path.Combine(repoRoot, "spec", "openapi.json");

// 1. Fetch the spec once and persist a pretty-printed snapshot. Saving a
// stable, indented copy makes "did the spec change?" diffs reviewable in PRs
// rather than walls of one-line JSON.
Console.WriteLine($"Fetching OpenAPI spec from {SpecUrl}...");
using var http = new HttpClient();
var rawJson = await http.GetStringAsync(SpecUrl);

var pretty = JsonNode.Parse(rawJson)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(specPath)!);
await File.WriteAllTextAsync(specPath, pretty);
Console.WriteLine($"Saved spec snapshot to {specPath} ({pretty.Length:N0} chars)");

// 2. Parse and run the C# client generator against the same JSON.
var document = await OpenApiDocument.FromJsonAsync(rawJson);
Console.WriteLine($"Loaded: {document.Info.Title} v{document.Info.Version} ({document.Paths.Count} paths)");

var settings = new CSharpClientGeneratorSettings
{
    // {controller} is replaced per-tag by NSwag → e.g. "DocumentsClient", "CompaniesClient".
    ClassName = "{controller}Client",

    // Group operations by their first OpenAPI tag, naming each method after
    // the operationId. The Recommand spec has 13 well-formed tags and unique
    // operationIds, so this produces one tidy client class per resource group.
    OperationNameGenerator = new MultipleClientsFromFirstTagAndOperationNameGenerator(),

    GenerateClientClasses = true,
    GenerateClientInterfaces = true,
    InjectHttpClient = true,

    // Branded exception so we don't collide with other SDKs' ApiException.
    ExceptionClass = "RecommandApiException",

    CSharpGeneratorSettings =
    {
        Namespace = "Recommand.Client",
        JsonLibrary = CSharpJsonLibrary.SystemTextJson,
        GenerateNullableReferenceTypes = true,
    }
};

var generator = new CSharpClientGenerator(document, settings);
var code = generator.GenerateFile();

Directory.CreateDirectory(generatedDir);
await File.WriteAllTextAsync(generatedPath, code);
Console.WriteLine($"Wrote {code.Length:N0} chars to {generatedPath}");
