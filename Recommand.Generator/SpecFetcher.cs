using System.Net.Http;

namespace Recommand.Generator;

internal static class SpecFetcher
{
    public static async Task<string> FetchAsync(string specUrl, string? localOverridePath)
    {
        if (!string.IsNullOrEmpty(localOverridePath))
        {
            Console.WriteLine($"Reading OpenAPI spec from local file {localOverridePath}...");
            return await File.ReadAllTextAsync(localOverridePath);
        }

        Console.WriteLine($"Fetching OpenAPI spec from {specUrl}...");
        using var http = new HttpClient();
        return await http.GetStringAsync(specUrl);
    }
}
