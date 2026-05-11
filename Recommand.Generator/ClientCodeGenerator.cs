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

        code = RewriteEnumConverter(code);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, code);
        Console.WriteLine($"Wrote {code.Length:N0} chars to {outputPath}");
    }

    /// <summary>
    /// Replace every reference to <c>System.Text.Json.Serialization.JsonStringEnumConverter&lt;T&gt;</c>
    /// with the SDK's <c>Recommand.Client.EnumMemberStringEnumConverter&lt;T&gt;</c>, which honours
    /// <c>[EnumMember(Value = "…")]</c> in both serialization directions.
    /// </summary>
    /// <remarks>
    /// NSwag emits the stock STJ converter as a per-property
    /// <c>[JsonConverter]</c> attribute. STJ's converter doesn't honour
    /// <c>EnumMember</c>, so values like Peppol scheme codes (<c>"0208"</c>)
    /// get serialized using the underscore-prefixed C# member name
    /// (<c>"_0208"</c>) because C# enum members can't start with a digit.
    /// Replacing the converter type fixes both the serialize direction
    /// (write <c>"0208"</c>) and the deserialize direction (parse <c>"0208"</c>
    /// back to <c>_0208</c>).
    /// </remarks>
    private static string RewriteEnumConverter(string code)
    {
        const string from = "System.Text.Json.Serialization.JsonStringEnumConverter<";
        const string to = "Recommand.Client.EnumMemberStringEnumConverter<";
        var rewrites = 0;
        var idx = 0;
        while ((idx = code.IndexOf(from, idx, StringComparison.Ordinal)) >= 0)
        {
            rewrites++;
            idx += from.Length;
        }
        Console.WriteLine($"Enum-converter rewrite: replaced {rewrites} references to JsonStringEnumConverter with EnumMemberStringEnumConverter.");
        return code.Replace(from, to);
    }
}
