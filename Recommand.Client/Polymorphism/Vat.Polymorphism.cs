using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recommand.Client;

[JsonConverter(typeof(VatJsonConverter))]
public partial class Vat
{
}

public partial class VatTotals : Vat
{
}

public partial class VatTotalsAutoCalculation : Vat
{
}

internal sealed class VatJsonConverter : JsonConverter<Vat>
{
    public override Vat? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var json = root.GetRawText();
        return root.TryGetProperty("totalVatAmount", out _)
            ? JsonSerializer.Deserialize<VatTotals>(json, options)
            : JsonSerializer.Deserialize<VatTotalsAutoCalculation>(json, options);
    }

    public override void Write(Utf8JsonWriter writer, Vat value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        if (value.GetType() == typeof(Vat))
        {
            throw new JsonException(
                "Vat is a polymorphic base. Assign a VatTotals or VatTotalsAutoCalculation instance instead.");
        }
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
