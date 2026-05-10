using System.Text;
using NJsonSchema;
using NJsonSchema.CodeGeneration;

namespace Recommand.Generator.Naming;

/// <summary>
/// Converts JSON enum values to PascalCase C# member names. NJsonSchema's
/// default <see cref="DefaultEnumNameGenerator"/> only uppercases the first
/// letter, producing names like <c>When_no_pdf_attachment</c> for snake_case
/// wire values. This generator splits on common delimiters (<c>_</c>,
/// <c>-</c>, <c>.</c>, <c>/</c>, space) and PascalCases each segment, while
/// the wire value is still preserved via <c>[EnumMember(Value = ...)]</c>.
/// </summary>
internal sealed class PascalCaseEnumNameGenerator : IEnumNameGenerator
{
    private static readonly char[] Separators = { '_', '-', '.', '/', ' ', ':' };

    public string Generate(int index, string? name, object? value, JsonSchema schema)
    {
        if (string.IsNullOrEmpty(name)) return "Empty";

        var sb = new StringBuilder(name.Length);
        var capitalizeNext = true;
        foreach (var ch in name)
        {
            if (Array.IndexOf(Separators, ch) >= 0)
            {
                capitalizeNext = true;
                continue;
            }
            if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(ch));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(ch);
            }
        }

        // C# enum members can't start with a digit; prefix an underscore.
        if (sb.Length > 0 && char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.Length == 0 ? "Empty" : sb.ToString();
    }
}
