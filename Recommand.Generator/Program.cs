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

// 3. Give every inline schema a sensible title so NSwag generates
// CreateCompanyRequest / CreateCompanyResponse instead of Body / Body2 /
// Response5. NSwag's default type-name generator already prefers schema.Title
// over its counter fallback, so simply assigning titles here is enough — no
// custom ITypeNameGenerator required.
//
// We only touch inline schemas (those without a $ref). Schemas resolved
// through $ref already have a name from components.schemas, and overwriting
// their Title would also rename the canonical type globally.
var renamedTitles = 0;
foreach (var pathItem in document.Paths.Values)
{
    foreach (var (httpMethod, operation) in pathItem.ActualPathItem)
    {
        var operationId = operation.OperationId;
        if (string.IsNullOrEmpty(operationId)) continue;
        var pascalOpId = char.ToUpperInvariant(operationId[0]) + operationId.Substring(1);

        // Request body
        if (operation.RequestBody?.Content.TryGetValue("application/json", out var requestContent) == true
            && requestContent.Schema is { } requestSchema
            && requestSchema.Reference is null
            && string.IsNullOrEmpty(requestSchema.Title))
        {
            requestSchema.Title = $"{pascalOpId}Request";
            renamedTitles++;
        }

        // Responses (one per status code)
        foreach (var (status, response) in operation.Responses)
        {
            if (!response.Content.TryGetValue("application/json", out var responseContent)) continue;
            if (responseContent.Schema is not { } responseSchema) continue;
            if (responseSchema.Reference is not null) continue;
            if (!string.IsNullOrEmpty(responseSchema.Title)) continue;

            // Success codes (2xx) become return types — keep the suffix simple.
            // Errors keep the status code so multiple error shapes don't collide.
            var isSuccess = status.Length == 3 && status[0] == '2';
            responseSchema.Title = isSuccess
                ? $"{pascalOpId}Response"
                : $"{pascalOpId}Response{status}";
            renamedTitles++;
        }
    }
}
Console.WriteLine($"Assigned titles to {renamedTitles} inline schemas");

var settings = new CSharpClientGeneratorSettings
{
    // {controller} is replaced per-tag by NSwag → e.g. "DocumentsClient", "CompaniesClient".
    ClassName = "{controller}Client",

    // Group operations by their first OpenAPI tag and PascalCase the tag for
    // the controller name. Built-in NSwag generators only do
    // `tag.Replace(' ', '_')`, which gives ugly names like
    // "Company_Document_TypesClient"; this custom generator produces
    // "CompanyDocumentTypesClient" instead.
    OperationNameGenerator = new PascalCaseTagOperationNameGenerator(),

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

/// <summary>
/// Groups operations by their first OpenAPI tag, PascalCasing the tag for the
/// generated controller name. Operation names come from the spec's
/// <c>operationId</c> field, capitalised so they fit C# method-name conventions.
/// </summary>
internal sealed class PascalCaseTagOperationNameGenerator : IOperationNameGenerator
{
    public bool SupportsMultipleClients => true;

    public string GetClientName(OpenApiDocument document, string path, string httpMethod, OpenApiOperation operation)
    {
        var tag = operation.Tags?.FirstOrDefault() ?? "Default";
        return PascalCase(tag);
    }

    public string GetOperationName(OpenApiDocument document, string path, string httpMethod, OpenApiOperation operation)
    {
        var raw = !string.IsNullOrEmpty(operation.OperationId)
            ? operation.OperationId
            : $"{httpMethod}{path.Replace("/", "_").Replace("{", "").Replace("}", "")}";
        return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
    }

    private static string PascalCase(string input)
    {
        var parts = input.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
