using NSwag;
using NSwag.CodeGeneration.OperationNameGenerators;

namespace Recommand.Generator.Naming;

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
        var parts = input.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
