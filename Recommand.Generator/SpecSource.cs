using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recommand.Generator;

/// <summary>
/// Loads the spec the generator works from: the upstream snapshot in
/// <c>spec/openapi.json</c> plus the hand-maintained supplements next to it.
/// </summary>
internal static class SpecSource
{
    public const string UpstreamUrl = "https://peppol.recommand.eu/openapi";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Downloads the live spec and overwrites the snapshot, unmodified apart
    /// from pretty-printing (so diffs between snapshots are reviewable).
    /// </summary>
    public static async Task RefreshSnapshotAsync(string snapshotPath)
    {
        Console.WriteLine($"Fetching {UpstreamUrl}");
        using var http = new HttpClient();
        var raw = await http.GetStringAsync(UpstreamUrl);
        var pretty = JsonNode.Parse(raw)!.ToJsonString(Pretty) + "\n";
        await File.WriteAllTextAsync(snapshotPath, pretty);
        Console.WriteLine($"Wrote snapshot to {snapshotPath} ({pretty.Length:N0} chars)");
    }

    /// <summary>
    /// Reads the snapshot and merges every supplement's
    /// <c>components.schemas</c> into it. A supplement may not redefine a
    /// schema the upstream spec already has: that would silently shadow it.
    /// </summary>
    public static async Task<JsonObject> LoadAsync(string snapshotPath, params string[] supplementPaths)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!.AsObject();
        var schemas = root["components"]!["schemas"]!.AsObject();

        foreach (var path in supplementPaths)
        {
            var supplement = JsonNode.Parse(await File.ReadAllTextAsync(path))!["components"]!["schemas"]!.AsObject();
            foreach (var (name, schema) in supplement)
            {
                if (schemas.ContainsKey(name))
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(path)} defines '{name}', which the upstream spec now defines too. " +
                        "Remove it from the supplement.");
                schemas[name] = schema!.DeepClone();
            }
            Console.WriteLine($"Merged {supplement.Count} schemas from {Path.GetFileName(path)}");
        }

        return root;
    }

    /// <summary>
    /// Gives a union its own copy of a variant that another union also uses.
    /// C# has single inheritance, so one class cannot be a subtype of two
    /// polymorphic bases; the copy becomes the subtype of
    /// <paramref name="union"/> and the original stays with the other one.
    /// </summary>
    public static void CloneVariantForUnion(JsonObject root, string union, string variant, string cloneName)
    {
        var schemas = root["components"]!["schemas"]!.AsObject();
        if (schemas[union] is not JsonObject unionSchema || schemas[variant] is not JsonObject variantSchema)
        {
            Console.WriteLine($"Clone {variant} for {union}: schema not found, skipped.");
            return;
        }

        var oldRef = "#/components/schemas/" + variant;
        var newRef = "#/components/schemas/" + cloneName;
        var replaced = 0;

        foreach (var member in unionSchema["oneOf"]?.AsArray() ?? [])
        {
            if (member?["$ref"]?.GetValue<string>() == oldRef) { member["$ref"] = newRef; replaced++; }
        }
        if (unionSchema["discriminator"]?["mapping"] is JsonObject mapping)
        {
            foreach (var key in mapping.Select(kv => kv.Key).ToList())
            {
                if (mapping[key]?.GetValue<string>() == oldRef) mapping[key] = newRef;
            }
        }

        if (replaced == 0)
        {
            Console.WriteLine($"Clone {variant} for {union}: {union} no longer references it, skipped.");
            return;
        }
        schemas[cloneName] = variantSchema.DeepClone();
        Console.WriteLine($"Cloned {variant} as {cloneName} for {union}.");
    }
}
