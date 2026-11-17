namespace Recommand.Generator.Naming;

internal static class Names
{
    private static readonly char[] Separators = ['_', '-', '.', '/', ' ', ':'];

    /// <summary><c>delivery_status.changed</c> → <c>DeliveryStatusChanged</c>; <c>creditNote</c> → <c>CreditNote</c>.</summary>
    public static string Pascal(string value) =>
        string.Concat(value
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
