using NSwag;
using NSwag.CodeGeneration.OperationNameGenerators;

namespace Recommand.Generator.Naming;

/// <summary>One client per OpenAPI tag; methods named after the operation id.</summary>
internal sealed class PascalCaseTagOperationNameGenerator : IOperationNameGenerator
{
    public bool SupportsMultipleClients => true;

    public string GetClientName(OpenApiDocument document, string path, string httpMethod, OpenApiOperation operation) =>
        PascalCase(operation.Tags?.FirstOrDefault() ?? "Default");

    public string GetOperationName(OpenApiDocument document, string path, string httpMethod, OpenApiOperation operation)
    {
        var raw = !string.IsNullOrEmpty(operation.OperationId)
            ? operation.OperationId
            : $"{httpMethod}{path.Replace("/", "_").Replace("{", "").Replace("}", "")}";
        return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
    }

    public static string PascalCase(string tag) => Names.Pascal(tag);
}
