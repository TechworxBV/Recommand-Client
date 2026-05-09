using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using NSwag;
using NSwag.CodeGeneration.CSharp;
using NSwag.CodeGeneration.OperationNameGenerators;

const string SpecUrl = "https://peppol.recommand.eu/openapi";

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var generatedDir = Path.Combine(repoRoot, "Recommand.Client", "Generated");
var generatedPath = Path.Combine(generatedDir, "RecommandClient.g.cs");
var specPath = Path.Combine(repoRoot, "spec", "openapi.json");

string rawJson;
var localOverride = Environment.GetEnvironmentVariable("RECOMMAND_SPEC_PATH");
if (!string.IsNullOrEmpty(localOverride))
{
    Console.WriteLine($"Reading OpenAPI spec from local file {localOverride}...");
    rawJson = await File.ReadAllTextAsync(localOverride);
}
else
{
    Console.WriteLine($"Fetching OpenAPI spec from {SpecUrl}...");
    using var http = new HttpClient();
    rawJson = await http.GetStringAsync(SpecUrl);
}

var pretty = JsonNode.Parse(rawJson)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(specPath)!);
await File.WriteAllTextAsync(specPath, pretty);
Console.WriteLine($"Saved spec snapshot to {specPath} ({pretty.Length:N0} chars)");

var document = await OpenApiDocument.FromJsonAsync(rawJson);
Console.WriteLine($"Loaded: {document.Info.Title} v{document.Info.Version} ({document.Paths.Count} paths)");

var renamedTitles = 0;
foreach (var pathItem in document.Paths.Values)
{
    foreach (var (httpMethod, operation) in pathItem.ActualPathItem)
    {
        var operationId = operation.OperationId;
        if (string.IsNullOrEmpty(operationId)) continue;
        var pascalOpId = char.ToUpperInvariant(operationId[0]) + operationId.Substring(1);

        if (operation.RequestBody?.Content.TryGetValue("application/json", out var requestContent) == true
            && requestContent.Schema is { } requestSchema
            && requestSchema.Reference is null
            && string.IsNullOrEmpty(requestSchema.Title))
        {
            requestSchema.Title = $"{pascalOpId}Request";
            renamedTitles++;
        }

        foreach (var (status, response) in operation.Responses)
        {
            if (!response.Content.TryGetValue("application/json", out var responseContent)) continue;
            if (responseContent.Schema is not { } responseSchema) continue;
            if (responseSchema.Reference is not null) continue;
            if (!string.IsNullOrEmpty(responseSchema.Title)) continue;

            var isSuccess = status.Length == 3 && status[0] == '2';
            responseSchema.Title = isSuccess
                ? $"{pascalOpId}Response"
                : $"{pascalOpId}Response{status}";
            renamedTitles++;
        }
    }
}
Console.WriteLine($"Assigned titles to {renamedTitles} inline schemas");

NormalizeSendDocumentPolymorphism(document);
NormalizeVatProperties(document);
HoistInlineNestedObjectsTyped(document);

var settings = new CSharpClientGeneratorSettings
{
    ClassName = "{controller}Client",
    OperationNameGenerator = new PascalCaseTagOperationNameGenerator(),
    GenerateClientClasses = true,
    GenerateClientInterfaces = true,
    InjectHttpClient = true,
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

static void HoistInlineNestedObjectsTyped(OpenApiDocument document)
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

    Console.WriteLine($"Hoist normalizer: extracted {hoisted} nested inline objects to definitions.");

    static void HoistContainer(JsonSchema container, string contextName, OpenApiDocument document, ref int counter)
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
            var newName = ChooseUnique(document.Definitions, $"{contextName}{pascalProp}");

            var hoistedSchema = new JsonSchema { Type = JsonObjectType.Object, Title = newName };
            foreach (var (k, v) in prop.Properties.ToList())
                hoistedSchema.Properties.Add(k, v);
            foreach (var r in prop.RequiredProperties.ToList())
                hoistedSchema.RequiredProperties.Add(r);
            if (!string.IsNullOrEmpty(prop.Description)) hoistedSchema.Description = prop.Description;

            document.Definitions[newName] = hoistedSchema;
            counter++;

            HoistContainer(hoistedSchema, newName, document, ref counter);

            actual.Properties[propName] = new JsonSchemaProperty { Reference = hoistedSchema };
        }
    }

    static string ChooseUnique(IDictionary<string, JsonSchema> defs, string preferred)
    {
        if (!defs.ContainsKey(preferred)) return preferred;
        for (var i = 2; ; i++)
        {
            var candidate = preferred + i;
            if (!defs.ContainsKey(candidate)) return candidate;
        }
    }
}

static void NormalizeSendDocumentPolymorphism(OpenApiDocument document)
{
    const string SendPath = "/api/v1/{companyId}/send";

    if (!document.Paths.TryGetValue(SendPath, out var pathItem)) return;
    if (!pathItem.ActualPathItem.TryGetValue("post", out var op)) return;
    if (op.RequestBody is null) return;
    if (!op.RequestBody.Content.TryGetValue("application/json", out var content)) return;
    if (content.Schema is not { } bodySchema) return;

    if (document.Definitions.TryGetValue("SendDocumentRequest", out var existing)
        && existing.DiscriminatorObject is not null
        && existing.Properties.Count > 0)
    {
        return;
    }

    var inlineProps = bodySchema.ActualSchema.Properties;
    if (inlineProps is null || inlineProps.Count == 0) return;

    var variants = new (string Discriminator, string RequestName, string DocumentSchema)[]
    {
        ("invoice",                "SendInvoiceRequest",                "SendInvoice"),
        ("creditNote",             "SendCreditNoteRequest",             "SendCreditNote"),
        ("selfBillingInvoice",     "SendSelfBillingInvoiceRequest",     "SendSelfBillingInvoice"),
        ("selfBillingCreditNote",  "SendSelfBillingCreditNoteRequest",  "SendSelfBillingCreditNote"),
        ("messageLevelResponse",   "SendMessageLevelResponseRequest",   "SendMessageLevelResponse"),
        ("xml",                    "SendXmlRequest",                    "XML"),
    };

    var sendBase = new JsonSchema { Type = JsonObjectType.Object, Title = "Send Document Request" };
    foreach (var (key, prop) in inlineProps)
    {
        if (key is "documentType" or "document") continue;
        sendBase.Properties.Add(key, prop);
    }
    sendBase.RequiredProperties.Add("recipient");
    sendBase.DiscriminatorObject = new OpenApiDiscriminator { PropertyName = "documentType" };
    document.Definitions["SendDocumentRequest"] = sendBase;

    foreach (var v in variants)
    {
        if (!document.Definitions.TryGetValue(v.DocumentSchema, out var docSchema)) continue;

        var variant = new JsonSchema { Title = "Send " + v.RequestName.Replace("Send", "").Replace("Request", "") };
        variant.AllOf.Add(new JsonSchema { Reference = sendBase });
        var addOn = new JsonSchema { Type = JsonObjectType.Object };
        addOn.Properties.Add("document", new JsonSchemaProperty { Reference = docSchema });
        addOn.RequiredProperties.Add("document");
        variant.AllOf.Add(addOn);

        document.Definitions[v.RequestName] = variant;
        sendBase.DiscriminatorObject.Mapping.Add(v.Discriminator, variant);
    }

    bodySchema.AnyOf.Clear();
    bodySchema.OneOf.Clear();
    bodySchema.Properties.Clear();
    bodySchema.Reference = sendBase;

    Console.WriteLine($"Send-document normalizer: rewrote send body to allOf inheritance with {variants.Length} variants.");
}

static void NormalizeVatProperties(OpenApiDocument document)
{
    string[] documentSchemas =
    [
        "Invoice", "CreditNote", "SelfBillingInvoice", "SelfBillingCreditNote",
        "SendInvoice", "SendCreditNote", "SendSelfBillingInvoice", "SendSelfBillingCreditNote",
    ];

    if (!document.Definitions.TryGetValue("VatTotals", out var vatTotals)) return;

    var rewrites = 0;
    foreach (var name in documentSchemas)
    {
        if (!document.Definitions.TryGetValue(name, out var schema)) continue;
        if (!schema.ActualProperties.TryGetValue("vat", out var vatProp)) continue;

        var union = vatProp.AnyOf.Concat(vatProp.OneOf).ToList();
        if (union.Count == 0) continue;
        if (!union.Any(s => s.HasReference && s.Reference == vatTotals)) continue;

        vatProp.AnyOf.Clear();
        vatProp.OneOf.Clear();
        vatProp.Reference = vatTotals;
        rewrites++;
    }

    Console.WriteLine($"Vat normalizer: rewrote {rewrites} vat properties to plain $ref to VatTotals.");
}

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
