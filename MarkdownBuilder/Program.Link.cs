using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Web;

internal partial class Program
{
    private record LinkedTitleModel(TitleModel TitleModel, string HtmlFilePath);

    private record struct TitleModelReference(LinkedTitleModel LinkedTitleModel, string? ReferenceTitle);
    
    private static async Task<FrozenDictionary<string, TitleModelReference>> LinkWikiStructureAsync(
        string tempArchiveDir,
        ConcurrentBag<string> htmlPaths)
    {
        var titles = new List<TitleModel>();

        await ParseTitles(tempArchiveDir, titles);

        var titleHtmlFileDictionary = htmlPaths
            .Select(
                path =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    var decodeName = HttpUtility.UrlDecode(fileName);
                    var indexOf = decodeName.IndexOf(':');
                    var parsedName = decodeName[(indexOf + 1)..];
                    var namespaceIndex = int.Parse(
                        decodeName.AsSpan(2, indexOf - 2)
                    );
                    return (namespaceIndex, parsedName, path);
                }
            )
            .GroupBy(x => x.namespaceIndex)
            .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.parsedName, y => y.path));

        var redirectWikis = new HashSet<TitleModel>();
        var titleDictionary = new Dictionary<string, TitleModelReference>();

        foreach (var titleModel in titles)
        {
            if (!string.IsNullOrWhiteSpace(titleModel.Redirect))
            {
                redirectWikis.Add(titleModel);
                continue;
            }

            if (!titleHtmlFileDictionary.TryGetValue(titleModel.Namespace, out var filePathDictionary) || 
                !filePathDictionary.TryGetValue(titleModel.Key, out var filePath))
            {
                Console.WriteLine($"Unable to find html source file for title: {titleModel}");
                continue;
            }

            titleDictionary.Add(
                titleModel.Title,
                new TitleModelReference(
                    new LinkedTitleModel(
                        titleModel,
                        filePath
                    ),
                    null
                )
            );
        }

        foreach (var titleModel in redirectWikis)
        {
            var redirect = titleModel.Redirect;
            string? redirectReference = null;
            if (redirect.Contains('#'))
            {
                var redirectSplit = redirect.Split('#', 2);
                redirect = redirectSplit[0];
                redirectReference = redirectSplit[1];
            }
            if (!titleDictionary.TryGetValue(redirect, out var redirectTarget))
            {
                Console.WriteLine($"Unable to find redirect target for title: {titleModel}");
                continue;
            }

            titleDictionary.Add(
                titleModel.Title,
                redirectTarget with { ReferenceTitle = redirectReference }
            );
        }

        return titleDictionary.ToFrozenDictionary();
    }
}