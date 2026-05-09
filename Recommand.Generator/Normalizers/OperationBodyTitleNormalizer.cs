using NSwag;

namespace Recommand.Generator.Normalizers;

internal sealed class OperationBodyTitleNormalizer : ISpecNormalizer
{
    public void Normalize(OpenApiDocument document)
    {
        var assigned = 0;
        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var (_, operation) in pathItem.ActualPathItem)
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
                    assigned++;
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
                    assigned++;
                }
            }
        }

        Console.WriteLine($"Operation-body titles: assigned {assigned} titles to inline operation body / response schemas.");
    }
}
