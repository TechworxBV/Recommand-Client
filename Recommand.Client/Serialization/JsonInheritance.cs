using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recommand.Client;

/// <summary>
/// Maps one wire discriminator value to a concrete subtype. Emitted by the
/// generator on every polymorphic base class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class JsonInheritanceAttribute : Attribute
{
    public JsonInheritanceAttribute(string key, Type type)
    {
        Key = key;
        Type = type;
    }

    /// <summary>The discriminator value as it appears in JSON.</summary>
    public string Key { get; }

    /// <summary>The subtype that value selects.</summary>
    public Type Type { get; }
}

/// <summary>
/// Marks a polymorphic base class and names the JSON property that selects
/// its subtype.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JsonInheritanceConverterAttribute : JsonConverterAttribute
{
    public JsonInheritanceConverterAttribute(Type baseType, string discriminatorName = "discriminator")
        : base(typeof(JsonInheritanceConverter<>).MakeGenericType(baseType))
    {
        DiscriminatorName = discriminatorName;
    }

    /// <summary>Name of the discriminator property in JSON.</summary>
    public string DiscriminatorName { get; }
}

/// <summary>
/// Discriminator-based polymorphic converter, used in place of the one NSwag
/// would emit. The difference: a discriminator value this SDK version has no
/// subtype for deserialises to the generated <c>Unknown{Base}</c> subtype
/// instead of throwing, so a document type or webhook event added to the API
/// later does not break the calls that return it. The unrecognised value
/// stays readable through <c>AdditionalProperties</c> and
/// <see cref="GetDiscriminator"/>.
/// </summary>
public sealed class JsonInheritanceConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    private static readonly Dictionary<string, Type> KeyToType = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> TypeToKey = new();

    // The generator emits one `Unknown{Base}` subtype per hierarchy. It has no
    // converter of its own, so System.Text.Json reads and writes it with the
    // plain object contract. NSwag lists it among the JsonInheritance
    // attributes keyed by its own type name, which is how it is found here.
    private static readonly Type? FallbackType;

    static JsonInheritanceConverter()
    {
        var fallbackName = "Unknown" + typeof(TBase).Name;
        foreach (var attribute in typeof(TBase).GetCustomAttributes<JsonInheritanceAttribute>(inherit: false))
        {
            if (attribute.Type.Name == fallbackName && attribute.Key == fallbackName)
            {
                FallbackType = attribute.Type;
                continue;
            }
            KeyToType[attribute.Key] = attribute.Type;
            TypeToKey[attribute.Type] = attribute.Key;
        }
    }

    /// <summary>Name of the discriminator property in JSON.</summary>
    public static string DiscriminatorName { get; } =
        typeof(TBase).GetCustomAttribute<JsonInheritanceConverterAttribute>(inherit: false)?.DiscriminatorName
        ?? "discriminator";

    /// <summary>Discriminator values this SDK version has a subtype for.</summary>
    public static IReadOnlyCollection<string> KnownDiscriminators => KeyToType.Keys;

    /// <summary>
    /// The wire discriminator of <paramref name="value"/>: the registered key
    /// for a known subtype, otherwise whatever arrived in the JSON.
    /// </summary>
    public static string? GetDiscriminator(TBase value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (TypeToKey.TryGetValue(value.GetType(), out var key)) return key;

        var extension = value.GetType().GetProperty("AdditionalProperties")?.GetValue(value);
        return extension is IDictionary<string, object> properties
               && properties.TryGetValue(DiscriminatorName, out var raw)
            ? raw?.ToString()
            : null;
    }

    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        Type? subtype = null;
        string? discriminator = null;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(DiscriminatorName, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            discriminator = value.GetString();
            KeyToType.TryGetValue(discriminator!, out subtype);
        }

        subtype ??= FallbackType
            ?? throw new JsonException(
                $"Could not find a subtype of {typeof(TBase).Name} for {DiscriminatorName} '{discriminator}'.");

        return (TBase?)JsonSerializer.Deserialize(root.GetRawText(), subtype, options);
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        if (type == typeof(TBase))
        {
            throw new JsonException(
                $"{type.Name} is a polymorphic base. Use one of its subtypes instead.");
        }

        using var members = JsonSerializer.SerializeToDocument(value, type, options);

        if (!TypeToKey.TryGetValue(type, out var key))
        {
            // Unknown{Base}: the discriminator, if any, is among its
            // additional properties and is written with the other members.
            members.RootElement.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString(DiscriminatorName, key);
        foreach (var property in members.RootElement.EnumerateObject())
        {
            if (!property.NameEquals(DiscriminatorName)) property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
