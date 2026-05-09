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

var specRoot = (JsonObject)JsonNode.Parse(rawJson)!;
var pretty = specRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(specPath)!);
await File.WriteAllTextAsync(specPath, pretty);
Console.WriteLine($"Saved spec snapshot to {specPath} ({pretty.Length:N0} chars)");

ApplySendDocumentPolymorphismShim(specRoot);
ApplyVatPolymorphismShim(specRoot);
HoistInlineNestedObjects(specRoot);

var processedJson = specRoot.ToJsonString();
var document = await OpenApiDocument.FromJsonAsync(processedJson);
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

static void ApplySendDocumentPolymorphismShim(JsonObject spec)
{
    const string SendPath = "/api/v1/{companyId}/send";

    var contentNode = spec["paths"]?[SendPath]?["post"]?["requestBody"]?["content"]?["application/json"];
    if (contentNode is not JsonObject content || content["schema"] is not JsonObject bodySchema)
    {
        return;
    }

    if (spec["components"] is not JsonObject components)
    {
        components = new JsonObject();
        spec["components"] = components;
    }
    if (components["schemas"] is not JsonObject schemas)
    {
        schemas = new JsonObject();
        components["schemas"] = schemas;
    }

    var existingSdr = schemas["SendDocumentRequest"] as JsonObject;
    if (existingSdr is not null
        && existingSdr["type"]?.GetValue<string>() == "object"
        && existingSdr.ContainsKey("discriminator")
        && existingSdr.ContainsKey("properties"))
    {
        Console.WriteLine("Send-document shim: already in allOf inheritance pattern, skipping.");
        return;
    }

    JsonObject? sourceProps = null;
    if (bodySchema["properties"] is JsonObject inlineProps)
    {
        sourceProps = inlineProps;
    }
    else if (bodySchema.ContainsKey("$ref") && existingSdr?["oneOf"] is JsonArray oneOfArr && oneOfArr.Count > 0)
    {
        var firstRef = (oneOfArr[0] as JsonObject)?["$ref"]?.GetValue<string>();
        var firstVariantName = firstRef?.Split('/').Last();
        if (firstVariantName is not null)
        {
            sourceProps = (schemas[firstVariantName] as JsonObject)?["properties"] as JsonObject;
        }
    }

    if (sourceProps is null)
    {
        Console.WriteLine("Send-document shim: couldn't locate envelope properties, skipping.");
        return;
    }

    Console.WriteLine("Send-document shim: rewriting to allOf inheritance pattern.");

    var variants = new (string Discriminator, string RequestName, string DocumentSchema, string Title)[]
    {
        ("invoice",                "SendInvoiceRequest",                "SendInvoice",                "Send Invoice"),
        ("creditNote",             "SendCreditNoteRequest",             "SendCreditNote",             "Send Credit Note"),
        ("selfBillingInvoice",     "SendSelfBillingInvoiceRequest",     "SendSelfBillingInvoice",     "Send Self-Billing Invoice"),
        ("selfBillingCreditNote",  "SendSelfBillingCreditNoteRequest",  "SendSelfBillingCreditNote",  "Send Self-Billing Credit Note"),
        ("messageLevelResponse",   "SendMessageLevelResponseRequest",   "SendMessageLevelResponse",   "Send Message Level Response"),
        ("xml",                    "SendXmlRequest",                    "XML",                        "Send XML"),
    };

    var envelopeProps = new JsonObject();
    foreach (var (key, value) in sourceProps)
    {
        if (key is "documentType" or "document") continue;
        envelopeProps[key] = value!.DeepClone();
    }

    var discriminatorMapping = new JsonObject();
    foreach (var v in variants)
    {
        discriminatorMapping[v.Discriminator] = $"#/components/schemas/{v.RequestName}";
    }

    schemas["SendDocumentRequest"] = new JsonObject
    {
        ["type"] = "object",
        ["required"] = new JsonArray("recipient"),
        ["properties"] = envelopeProps,
        ["discriminator"] = new JsonObject
        {
            ["propertyName"] = "documentType",
            ["mapping"] = discriminatorMapping,
        },
        ["title"] = "Send Document Request",
    };

    foreach (var v in variants)
    {
        schemas[v.RequestName] = new JsonObject
        {
            ["allOf"] = new JsonArray
            {
                new JsonObject { ["$ref"] = "#/components/schemas/SendDocumentRequest" },
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("document"),
                    ["properties"] = new JsonObject
                    {
                        ["document"] = new JsonObject
                        {
                            ["$ref"] = $"#/components/schemas/{v.DocumentSchema}",
                        },
                    },
                },
            },
            ["title"] = v.Title,
        };
    }

    content["schema"] = new JsonObject
    {
        ["$ref"] = "#/components/schemas/SendDocumentRequest",
    };
}

static void ApplyVatPolymorphismShim(JsonObject spec)
{
    var schemas = (spec["components"] as JsonObject)?["schemas"] as JsonObject;
    if (schemas is null) return;

    if (schemas.ContainsKey("Vat"))
    {
        Console.WriteLine("Vat shim: Vat schema already present, skipping.");
        return;
    }

    string[] readShapes = ["Invoice", "CreditNote", "SelfBillingInvoice", "SelfBillingCreditNote"];
    string[] sendShapes = ["SendInvoice", "SendCreditNote", "SendSelfBillingInvoice", "SendSelfBillingCreditNote"];

    var readRewrites = RewriteReadVatShapes(schemas, readShapes);
    var sendRewrites = RewriteSendVatShapes(schemas, sendShapes);

    if (sendRewrites > 0)
    {
        schemas["Vat"] = new JsonObject
        {
            ["type"] = "object",
            ["title"] = "Vat",
            ["description"] = "Either VatTotals (explicit amounts) or VatTotalsAutoCalculation. Discriminated by property presence at runtime by VatJsonConverter.",
        };
    }

    Console.WriteLine($"Vat shim: rewrote {readRewrites} read shapes (→ VatTotals) and {sendRewrites} send shapes (→ Vat polymorphic base).");
}

static int RewriteReadVatShapes(JsonObject schemas, IEnumerable<string> documentNames)
{
    var rewrites = 0;
    foreach (var docName in documentNames)
    {
        if (schemas[docName] is not JsonObject docSchema) continue;
        if (docSchema["properties"] is not JsonObject props) continue;
        if (props["vat"] is not JsonObject vatSchema) continue;

        var variants = vatSchema["oneOf"] as JsonArray ?? vatSchema["anyOf"] as JsonArray;
        if (variants is null) continue;

        var refs = variants.OfType<JsonObject>()
            .Select(o => o["$ref"]?.GetValue<string>()?.Split('/').Last())
            .Where(s => s is not null)
            .ToHashSet();

        if (!refs.Contains("VatTotals") || refs.Contains("VatTotalsAutoCalculation")) continue;

        props["vat"] = new JsonObject
        {
            ["$ref"] = "#/components/schemas/VatTotals",
        };
        rewrites++;
    }
    return rewrites;
}

static int RewriteSendVatShapes(JsonObject schemas, IEnumerable<string> documentNames)
{
    var rewrites = 0;
    foreach (var docName in documentNames)
    {
        if (schemas[docName] is not JsonObject docSchema) continue;
        if (docSchema["properties"] is not JsonObject props) continue;
        if (props["vat"] is not JsonObject vatSchema) continue;

        var variants = vatSchema["anyOf"] as JsonArray ?? vatSchema["oneOf"] as JsonArray;
        if (variants is null) continue;

        var refs = variants.OfType<JsonObject>()
            .Select(o => o["$ref"]?.GetValue<string>()?.Split('/').Last())
            .Where(s => s is not null)
            .ToHashSet();

        if (!refs.Contains("VatTotals") || !refs.Contains("VatTotalsAutoCalculation")) continue;

        props["vat"] = new JsonObject
        {
            ["$ref"] = "#/components/schemas/Vat",
        };
        rewrites++;
    }
    return rewrites;
}

static void HoistInlineNestedObjects(JsonObject spec)
{
    var components = spec["components"] as JsonObject ?? throw new InvalidOperationException("components missing");
    var schemas = components["schemas"] as JsonObject ?? throw new InvalidOperationException("components.schemas missing");

    var hoisted = 0;

    var seedNames = schemas.Select(kv => kv.Key).ToList();
    foreach (var schemaName in seedNames)
    {
        if (schemas[schemaName] is JsonObject schemaNode)
            HoistContainer(schemaNode, schemaName, schemas, ref hoisted);
    }

    if (spec["paths"] is JsonObject paths)
    {
        foreach (var (_, pathNode) in paths)
        {
            if (pathNode is not JsonObject pathItem) continue;
            foreach (var (_, opNode) in pathItem)
            {
                if (opNode is not JsonObject op) continue;
                var operationId = op["operationId"]?.GetValue<string>();
                if (string.IsNullOrEmpty(operationId)) continue;
                var pascalOpId = char.ToUpperInvariant(operationId[0]) + operationId.Substring(1);

                if (op["requestBody"]?["content"]?["application/json"]?["schema"] is JsonObject reqSchema)
                    HoistContainer(reqSchema, $"{pascalOpId}Request", schemas, ref hoisted);

                if (op["responses"] is JsonObject responses)
                {
                    foreach (var (status, respNode) in responses)
                    {
                        if (respNode is not JsonObject resp) continue;
                        if (resp["content"]?["application/json"]?["schema"] is JsonObject respSchema)
                        {
                            var ctx = (status.Length == 3 && status[0] == '2')
                                ? $"{pascalOpId}Response"
                                : $"{pascalOpId}Response{status}";
                            HoistContainer(respSchema, ctx, schemas, ref hoisted);
                        }
                    }
                }
            }
        }
    }

    Console.WriteLine($"Hoist shim: extracted {hoisted} nested inline objects to components.schemas with contextual names.");

    static void HoistContainer(JsonObject containerSchema, string contextName, JsonObject schemas, ref int counter)
    {
        var props = containerSchema["properties"] as JsonObject;
        if (props is null) return;

        foreach (var propName in props.Select(kv => kv.Key).ToList())
        {
            if (props[propName] is not JsonObject prop) continue;
            if (prop.ContainsKey("$ref")) continue;
            if (!IsObjectShape(prop)) continue;

            var pascalProp = char.ToUpperInvariant(propName[0]) + propName.Substring(1);
            var newName = ChooseUnique(schemas, $"{contextName}{pascalProp}");

            var clone = (JsonObject)prop.DeepClone();
            if (clone["type"] is JsonArray typeArr)
            {
                var nonNull = typeArr.OfType<JsonValue>().Select(v => v.GetValue<string>()).FirstOrDefault(t => t != "null");
                clone["type"] = nonNull ?? "object";
            }
            schemas[newName] = clone;
            counter++;

            HoistContainer(clone, newName, schemas, ref counter);

            props[propName] = new JsonObject { ["$ref"] = $"#/components/schemas/{newName}" };
        }
    }

    static bool IsObjectShape(JsonObject schema)
    {
        if (schema["properties"] is JsonObject p && p.Count > 0)
        {
            var t = schema["type"];
            if (t is JsonValue v && v.GetValue<string>() == "object") return true;
            if (t is JsonArray arr && arr.OfType<JsonValue>().Any(x => x.GetValue<string>() == "object")) return true;
        }
        return false;
    }

    static string ChooseUnique(JsonObject schemas, string preferred)
    {
        if (!schemas.ContainsKey(preferred)) return preferred;
        for (var i = 2; ; i++)
        {
            var candidate = preferred + i;
            if (!schemas.ContainsKey(candidate)) return candidate;
        }
    }
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
