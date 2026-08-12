using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Web;

internal partial class Program
{
    private record LinkedTitleModel(TitleModel TitleModel, string? HtmlFilePath);

    private record struct TitleModelReference(LinkedTitleModel LinkedTitleModel, string? ReferenceTitle);

    private static async Task<FrozenDictionary<string, TitleModelReference>> LinkWikiStructureAsync(
        string tempArchiveDir,
        ConcurrentBag<string> htmlPaths,
        BuildLog buildLog)
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

            string? filePath = null;
            if (!titleHtmlFileDictionary.TryGetValue(titleModel.Namespace, out var filePathDictionary) ||
                !filePathDictionary.TryGetValue(titleModel.Key, out filePath))
            {
                buildLog.AddMissingHtml(titleModel.ToString());
                // Still register the title so links resolve; page content will be a stub from titles.json.
                filePath = null;
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

            if (!TryResolveTitle(titleDictionary, redirect, out var redirectTarget))
            {
                buildLog.AddMissingRedirectTarget(titleModel.ToString());
                continue;
            }

            titleDictionary.Add(
                titleModel.Title,
                redirectTarget with { ReferenceTitle = redirectReference ?? redirectTarget.ReferenceTitle }
            );
        }

        return titleDictionary.ToFrozenDictionary();
    }

    private static bool TryResolveTitle(
        IReadOnlyDictionary<string, TitleModelReference> titleDictionary,
        string rawTitle,
        out TitleModelReference reference)
    {
        foreach (var candidate in EnumerateTitleLookupCandidates(rawTitle))
        {
            if (titleDictionary.TryGetValue(candidate, out reference))
                return true;
        }

        reference = default;
        return false;
    }

    private static IEnumerable<string> EnumerateTitleLookupCandidates(string rawTitle)
    {
        if (string.IsNullOrEmpty(rawTitle))
            yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slashVariant in ExpandSlashVariants(rawTitle))
        {
            foreach (var candidate in ExpandParenVariants(slashVariant.Replace(' ', '_')))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }

            // Some titles may literally contain spaces (rare in this dump, but cheap to try).
            foreach (var candidate in ExpandParenVariants(slashVariant.Replace('_', ' ')))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }

            if (seen.Add(slashVariant))
                yield return slashVariant;
        }
    }

    private static IEnumerable<string> ExpandSlashVariants(string title)
    {
        yield return title;

        // MediaWiki sometimes percent-encodes fullwidth slash U+FF0F as path segment.
        var toAscii = title.Replace('／', '/').Replace('＼', '\\');
        if (!string.Equals(toAscii, title, StringComparison.Ordinal))
            yield return toAscii;

        var toFullwidth = title.Replace('/', '／');
        if (!string.Equals(toFullwidth, title, StringComparison.Ordinal))
            yield return toFullwidth;
    }

    private static IEnumerable<string> ExpandParenVariants(string title)
    {
        yield return title;

        var toFullwidth = title.Replace('(', '（').Replace(')', '）');
        if (!string.Equals(toFullwidth, title, StringComparison.Ordinal))
            yield return toFullwidth;

        var toAscii = title.Replace('（', '(').Replace('）', ')');
        if (!string.Equals(toAscii, title, StringComparison.Ordinal))
            yield return toAscii;
    }
}
