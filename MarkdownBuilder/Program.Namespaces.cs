using System.Text.Json;
using System.Text.Json.Serialization;

internal partial class Program
{
    [JsonSerializable(typeof(Namespace[]))]
    private partial class NamespaceJsonContext : JsonSerializerContext;
    
    private class Namespace
    {
        [JsonPropertyName("id")] public int Id { get; init; }
        [JsonPropertyName("case")] public required string Case { get; init; }
        [JsonPropertyName("subpages")] public string? Subpages { get; init; }
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("*")] public required string DisplayName { get; init; }
        [JsonPropertyName("canonical")] public string? Canonical { get; init; }
        [JsonPropertyName("defaultcontentmodel")] public string? DefaultContentModel { get; init; }
        [JsonPropertyName("namespaceprotection")] public string? NamespaceProtection { get; init; }
    }

    
    private static async Task ParseNamespaces(string tempArchiveDir, List<Namespace> namespaces)
    {
        var namespacesJsonPath = Path.Combine(tempArchiveDir, "namespaces.json");
        var file = await File.ReadAllTextAsync(namespacesJsonPath);
        var namespaceArray = JsonSerializer.Deserialize(file, NamespaceJsonContext.Default.NamespaceArray)!;
        namespaces.AddRange(namespaceArray);
    }
}