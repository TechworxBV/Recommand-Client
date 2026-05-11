using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recommand.Client;

/// <summary>
/// String-based <see cref="JsonConverter{T}"/> for enums that respects
/// <see cref="EnumMemberAttribute"/> in both directions.
/// </summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c>'s built-in <c>JsonStringEnumConverter</c>
/// serializes the C# member name as-is and does <i>not</i> honour
/// <c>[EnumMember(Value = "…")]</c>. NSwag generates enums with the C#
/// member name picked by our <c>PascalCaseEnumNameGenerator</c> and the
/// wire value separately attached via <c>[EnumMember]</c>. For enum values
/// like Peppol scheme codes (<c>"0208"</c>, <c>"0002"</c>, …) the C# name
/// has to be prefixed with an underscore to be valid C# (<c>_0208</c>),
/// and that underscore leaks onto the wire under the stock converter
/// (server sees <c>"_0208"</c>, rejects it).
/// </para>
/// <para>
/// This converter pre-computes the bidirectional map between C# values
/// and wire strings using <see cref="EnumMemberAttribute.Value"/> when
/// present, falling back to the C# member name otherwise. It's emitted in
/// place of NSwag's converter by a post-process step in the generator.
/// </para>
/// </remarks>
public sealed class EnumMemberStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> _wireToEnum = BuildWireToEnum();
    private static readonly Dictionary<TEnum, string> _enumToWire = BuildEnumToWire();

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a JSON string for {typeof(TEnum).Name}; got {reader.TokenType}.");
        }

        var wire = reader.GetString();
        if (wire is null)
        {
            throw new JsonException($"Got a null JSON string for {typeof(TEnum).Name}.");
        }

        if (_wireToEnum.TryGetValue(wire, out var value)) return value;

        // Fallback: tolerate the underscore-prefixed C# member name too, so a
        // client mid-migration that accidentally double-encodes (e.g. proxied
        // through another SDK) doesn't blow up loudly. Strict-by-default
        // would require server contracts we don't yet rely on.
        if (Enum.TryParse<TEnum>(wire, ignoreCase: false, out var fallback)) return fallback;

        throw new JsonException($"Unknown {typeof(TEnum).Name} value '{wire}'.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        // Map → wire value. If for some reason the enum value wasn't in the
        // pre-built map (e.g. a forged int cast), fall back to the C# name.
        var wire = _enumToWire.TryGetValue(value, out var v) ? v : value.ToString();
        writer.WriteStringValue(wire);
    }

    private static Dictionary<string, TEnum> BuildWireToEnum()
    {
        var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        foreach (var (value, wire) in EnumerateMembers())
        {
            // Last-write-wins for any collisions; spec enums shouldn't collide.
            map[wire] = value;
        }
        return map;
    }

    private static Dictionary<TEnum, string> BuildEnumToWire()
    {
        var map = new Dictionary<TEnum, string>();
        foreach (var (value, wire) in EnumerateMembers())
        {
            map[value] = wire;
        }
        return map;
    }

    private static IEnumerable<(TEnum Value, string Wire)> EnumerateMembers()
    {
        foreach (var name in Enum.GetNames(typeof(TEnum)))
        {
            var value = (TEnum)Enum.Parse(typeof(TEnum), name);
            var field = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static)!;
            var enumMember = field.GetCustomAttribute<EnumMemberAttribute>();
            var wire = enumMember?.Value ?? name;
            yield return (value, wire);
        }
    }
}
