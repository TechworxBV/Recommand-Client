using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recommand.Generator;

internal static class SpecSnapshot
{
    public static async Task WriteAsync(string rawJson, string snapshotPath)
    {
        var pretty = JsonNode.Parse(rawJson)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        await File.WriteAllTextAsync(snapshotPath, pretty);
        Console.WriteLine($"Saved spec snapshot to {snapshotPath} ({pretty.Length:N0} chars)");
    }
}
