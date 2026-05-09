using NJsonSchema;
using NSwag;

namespace Recommand.Generator.Normalizers;

internal sealed class SendDocumentPolymorphismNormalizer : ISpecNormalizer
{
    private const string SendPath = "/api/v1/{companyId}/send";

    private static readonly (string Discriminator, string RequestName, string DocumentSchema)[] Variants =
    [
        ("invoice",                "SendInvoiceRequest",                "SendInvoice"),
        ("creditNote",             "SendCreditNoteRequest",             "SendCreditNote"),
        ("selfBillingInvoice",     "SendSelfBillingInvoiceRequest",     "SendSelfBillingInvoice"),
        ("selfBillingCreditNote",  "SendSelfBillingCreditNoteRequest",  "SendSelfBillingCreditNote"),
        ("messageLevelResponse",   "SendMessageLevelResponseRequest",   "SendMessageLevelResponse"),
        ("xml",                    "SendXmlRequest",                    "XML"),
    ];

    public void Normalize(OpenApiDocument document)
    {
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

        var sendBase = new JsonSchema { Type = JsonObjectType.Object, Title = "Send Document Request" };
        foreach (var (key, prop) in inlineProps)
        {
            if (key is "documentType" or "document") continue;
            sendBase.Properties.Add(key, prop);
        }
        sendBase.RequiredProperties.Add("recipient");
        sendBase.DiscriminatorObject = new OpenApiDiscriminator { PropertyName = "documentType" };
        document.Definitions["SendDocumentRequest"] = sendBase;

        foreach (var v in Variants)
        {
            if (!document.Definitions.TryGetValue(v.DocumentSchema, out var docSchema)) continue;

            var variant = new JsonSchema
            {
                Title = "Send " + v.RequestName.Replace("Send", "").Replace("Request", ""),
            };
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

        Console.WriteLine($"Send-document polymorphism: rewrote send body to allOf inheritance with {Variants.Length} variants.");
    }
}
