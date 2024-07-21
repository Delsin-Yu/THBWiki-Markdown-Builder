using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Web;

internal partial class Program
{
    private record LinkedTitleModel(TitleModel TitleModel, string HtmlFilePath);
    
    private static async Task<(FrozenDictionary<NamespaceModel, LinkedTitleModel[]>, FrozenDictionary<string, LinkedTitleModel>)> LinkWikiStructureAsync(string tempArchiveDir, ConcurrentBag<string> paths)
    {
        var namespaces = new List<NamespaceModel>();
        var titles = new List<TitleModel>();

        await Task.WhenAll(
            ParseNamespaces(tempArchiveDir, namespaces),
            ParseTitles(tempArchiveDir, titles)
        );

        var namespaceLookup = namespaces.ToDictionary(x => x.Id);
        var wikiStructure = new Dictionary<NamespaceModel, ConcurrentBag<TitleModel>>();
        foreach (var @namespace in namespaces) wikiStructure.Add(@namespace, []);

        Parallel.ForEach(
            titles,
            model => wikiStructure[namespaceLookup[model.Namespace]].Add(model)
        );
        foreach (var (namespaceModel, titleModels) in wikiStructure.ToArray())
        {
            if (!titleModels.IsEmpty) continue;
            wikiStructure.Remove(namespaceModel);
        }

        var fileDictionary = paths
            .Select(
                path =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    var decodeName = HttpUtility.UrlDecode(fileName);
                    var indexOf = decodeName.IndexOf(':');
                    var namespaceIndex = int.Parse(
                        decodeName.AsSpan(2, indexOf - 2)
                    );
                    var parsedName = decodeName[(indexOf + 1)..];
                    return (namespaceIndex, parsedName, path);
                }
            )
            .GroupBy(x => x.namespaceIndex)
            .ToDictionary(x => namespaceLookup[x.Key], x => x.ToDictionary(y => y.parsedName, y => y.path));

        var linkedWikiStructure = new Dictionary<NamespaceModel, List<(TitleModel, string)>>();
        foreach (var (namespaceModel, titleModels) in wikiStructure)
        {
            var fileEntry = fileDictionary[namespaceModel];
            List<(TitleModel, string)> linkedItemModels = [];
            linkedWikiStructure.Add(namespaceModel, linkedItemModels);
            foreach (var titleModel in titleModels)
            {
                if (!string.IsNullOrWhiteSpace(titleModel.Redirect)) continue;
                if (!fileEntry.TryGetValue(titleModel.Key, out var filePath))
                {
                    // Console.WriteLine($"Unable to find: {namespaceModel.DisplayName} : {titleModel.Key}");
                    continue;
                }

                linkedItemModels.Add((titleModel, filePath));
            }
        }

        var bakedWikiDictionary = linkedWikiStructure.ToFrozenDictionary(x => x.Key, x => x.Value.Select(y => new LinkedTitleModel(y.Item1, y.Item2)).ToArray());
        var bakedTitleDictionary = bakedWikiDictionary.Values.SelectMany(x => x).ToFrozenDictionary(x => x.TitleModel.Title, x => x);

        return (bakedWikiDictionary, bakedTitleDictionary);
    }
}