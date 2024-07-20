using System.Text.Json;
using System.Text.Json.Serialization;

internal partial class Program
{
    [JsonSerializable(typeof(TitleModel[]))]
    private partial class TitleJsonContext : JsonSerializerContext;
    
    private class TitleModel
    {
        [JsonPropertyName("id")] public required int Id { get; init; }
        [JsonPropertyName("namespace")] public required int Namespace { get; init; }
        [JsonPropertyName("title")] public required string Title { get; init; }
        [JsonPropertyName("key")] public required string Key { get; init; }
        [JsonPropertyName("redirect")] public required string Redirect { get; init; }
        [JsonPropertyName("extract")] public required string Extract { get; init; }
        
        public override string ToString() => $"{Id:00}:{Title}";
    }

    
    private static async Task ParseTitles(string tempArchiveDir, List<TitleModel> titles)
    {
        var titlesJsonPath = Path.Combine(tempArchiveDir, "titles.json");
        var file = await File.ReadAllTextAsync(titlesJsonPath);
        var titleArray = JsonSerializer.Deserialize(file, TitleJsonContext.Default.TitleModelArray)!;
        titles.AddRange(titleArray);
    }
}