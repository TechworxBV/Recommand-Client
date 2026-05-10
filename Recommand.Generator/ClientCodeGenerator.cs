using NJsonSchema.CodeGeneration.CSharp;
using NSwag;
using NSwag.CodeGeneration.CSharp;
using Recommand.Generator.Naming;

namespace Recommand.Generator;

internal static class ClientCodeGenerator
{
    public static async Task GenerateAsync(OpenApiDocument document, string outputPath)
    {
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
                EnumNameGenerator = new PascalCaseEnumNameGenerator(),
            },
        };

        var generator = new CSharpClientGenerator(document, settings);
        var code = generator.GenerateFile();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, code);
        Console.WriteLine($"Wrote {code.Length:N0} chars to {outputPath}");
    }
}
