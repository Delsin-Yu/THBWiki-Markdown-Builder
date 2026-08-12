using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;

internal partial class Program
{
    private const string FileFooter =
        """
        ---

        此文档由 [THBWiki-Markdown-Builder](https://github.com/Delsin-Yu/THBWiki-Markdown-Builder) 构建。

        文档中的所有内容除特殊注明外，均在 [**知识共享(Creative Commons) 署名-非商业性使用-相同方式共享 3.0 协议**](https://creativecommons.org/licenses/by-sa/3.0/deed.zh-hans) 下提供，附加条款亦可能应用。

        引用类型与其他类型作品版权归原作者所有，如有作者授权则遵照授权协议使用。

        详细请查阅 [THBWiki：免责声明](https://thbwiki.cc/THBWiki:%E5%85%8D%E8%B4%A3%E5%A3%B0%E6%98%8E)。

        """;
    
    private static async Task BuildSinglePageAsync(
        string pageTitle,
        FrozenDictionary<string, TitleModelReference> titleModelLookup,
        string markdownDir,
        BuildLog buildLog)
    {
        const string relativeMainPageLink = "./readme.md";
        var lookupInfo = new LookupInfo(titleModelLookup, relativeMainPageLink, markdownDir, buildLog);
        lookupInfo.SeedExistingPages();

        Console.WriteLine($"Start Compiling Single Page: {pageTitle}");
        if (!lookupInfo.ForceRebuildPage(pageTitle, out var link))
            throw new InvalidOperationException($"Unable to resolve page title: {pageTitle}");

        Console.WriteLine($"Resolved to: {link}");
        Console.WriteLine("Waiting for Subpages Compilation");
        await lookupInfo.WaitForFinish();
    }

    private static async Task BuildPagesAsync(
        HyperLinkNode[] topPages,
        FrozenDictionary<string, TitleModelReference> titleModelLookup,
        string markdownDir,
        BuildLog buildLog)
    {
        var indexBuilder = new StringBuilder("# THBWiki - Markdown\n\n");

        var absoluteMainPageLink = Path.Combine(markdownDir, "readme.md");
        const string relativeMainPageLink = $"./readme.md";
        var lookupInfo = new LookupInfo(titleModelLookup, relativeMainPageLink, markdownDir, buildLog);

        Console.WriteLine("Start Compiling Pages");
        foreach (var page in topPages)
        {
            AppendCore("##", indexBuilder, page, 0, lookupInfo);
            indexBuilder.AppendLine();
            AppendChildren(indexBuilder, page, 0, lookupInfo);

            indexBuilder.AppendLine();
        }

        indexBuilder.AppendLine(FileFooter);

        await File.WriteAllTextAsync(absoluteMainPageLink, indexBuilder.ToString());

        Console.WriteLine("Waiting for Subpages Compilation");
        await lookupInfo.WaitForFinish();

        return;

        static void AppendChildren(
            StringBuilder builder,
            HyperLinkNode node,
            int level,
            LookupInfo lookupInfo
        )
        {
            foreach (var child in node.Children)
            {
                AppendCore("-", builder, child, level, lookupInfo);
                AppendChildren(builder, child, level + 1, lookupInfo);
            }
        }

        static void AppendCore(
            string header,
            StringBuilder builder,
            HyperLinkNode node,
            int level,
            LookupInfo lookupInfo)
        {
            builder
                .Append(' ', level * 2)
                .Append(header)
                .Append(' ');

            var displayName = node.DisplayName.Trim();

            if (node.Link != null)
            {
                if (!lookupInfo.TryCreateLink(node.RawLink!, true, out var link))
                    builder.AppendLine($"{displayName} (未找到链接)");
                else
                    builder.AppendLine($"[{displayName}]({link})");
            }
            else
            {
                builder.AppendLine(displayName);
            }

        }
    }

    private partial record LookupInfo(
        FrozenDictionary<string, TitleModelReference> TitleDictionary,
        string MainPageLink,
        string FileDirectory,
        BuildLog BuildLog)
    {
        private readonly ConcurrentDictionary<string, byte> _createdPages = [];

        private int _taskCount;
        private int _finishedTaskCount;

        [GeneratedRegex("""[\\/:*?""<>|]""")]
        private partial Regex GetReplaceFileNameRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceCollapseRegex();

        public async Task WaitForFinish()
        {
            while (_taskCount > 0)
            {
                await Task.Delay(1000);
                Console.WriteLine($"Remaining Tasks: {_taskCount}, Finished Tasks: {_finishedTaskCount}");
            }
        }

        public void SeedExistingPages()
        {
            var sourcesDir = Path.Combine(FileDirectory, "sources");
            if (!Directory.Exists(sourcesDir)) return;

            foreach (var file in Directory.EnumerateFiles(sourcesDir, "*.md"))
                _createdPages.TryAdd(Path.GetFileNameWithoutExtension(file), 0);
        }

        public bool ForceRebuildPage(string pageTitle, [NotNullWhen(true)] out string? link)
        {
            string? matchedKey = null;
            TitleModelReference titleModelReference = default;
            foreach (var candidate in EnumerateTitleLookupCandidates(pageTitle))
            {
                if (TitleDictionary.TryGetValue(candidate, out titleModelReference))
                {
                    matchedKey = candidate;
                    break;
                }
            }

            if (matchedKey is null)
            {
                link = null;
                return false;
            }

            var linkedTitleModel = titleModelReference.LinkedTitleModel;
            var titleModel = linkedTitleModel.TitleModel;
            var targetFileName = GetReplaceFileNameRegex().Replace(titleModel.Title, "-");
            // Redirect aliases keep their own corpus filenames (e.g. 东方三月精E.md); refresh those too.
            var aliasFileName = GetReplaceFileNameRegex().Replace(matchedKey, "-");

            _createdPages.TryRemove(targetFileName, out _);
            _createdPages.TryRemove(aliasFileName, out _);

            if (!string.Equals(aliasFileName, targetFileName, StringComparison.Ordinal))
                _ = CreatePageAsync(aliasFileName, linkedTitleModel, titleModel);

            return TryCreateLink("/" + matchedKey, true, out link);
        }

        public bool TryCreateLink(string rawHref, bool isRoot, [NotNullWhen(true)] out string? link)
        {
            var decoded = HttpUtility.HtmlDecode(HttpUtility.UrlDecode(rawHref));

            // Protocol-relative or odd rooted externals: //example.com/...
            if (decoded.StartsWith("//", StringComparison.Ordinal))
            {
                link = "https:" + decoded;
                return true;
            }

            if (!decoded.StartsWith('/'))
            {
                link = decoded;
                return true;
            }

            // Relative parent paths are not wiki article links.
            if (decoded.StartsWith("/..", StringComparison.Ordinal))
            {
                BuildLog.AddUnresolvedLink($"{decoded}, {rawHref}");
                link = null;
                return false;
            }

            decoded = decoded[1..];

            string? hrefFragment = null;
            if (decoded.Contains('#'))
            {
                var split = decoded.Split('#', 2);
                decoded = split[0];
                hrefFragment = split[1];
            }

            // index.php?title=... must be parsed before stripping queries.
            if (TryExtractIndexPhpTitle(decoded, out var indexPhpTitle))
                decoded = indexPhpTitle;
            else if (decoded.Contains('?'))
                // Drop SMW/filter query strings: /展会作品列表?e=Comic+Market%2374
                decoded = decoded.Split('?', 2)[0];

            if (!TryResolveTitle(TitleDictionary, decoded, out var titleModelReference))
            {
                BuildLog.AddUnresolvedLink($"{decoded}, {rawHref}");
                link = null;
                return false;
            }

            var linkedTitleModel = titleModelReference.LinkedTitleModel;
            var titleModel = linkedTitleModel.TitleModel;
            if (titleModel.Id == 1)
            {
                link = MainPageLink;
                return true;
            }

            var canonicalFileName = GetReplaceFileNameRegex().Replace(titleModel.Title, "-");
            var path = isRoot ? $"./sources/{canonicalFileName}.md" : $"./{canonicalFileName}.md";

            var fragment = hrefFragment ?? titleModelReference.ReferenceTitle;
            if (!string.IsNullOrEmpty(fragment))
                path += $"#{fragment}";

            // Key by sanitized filename so titles that collapse to the same path cannot race.
            if (_createdPages.TryAdd(canonicalFileName, 0))
            {
                _ = CreatePageAsync(canonicalFileName, linkedTitleModel, titleModel);
            }

            link = path;
            return true;
        }

        private static bool TryExtractIndexPhpTitle(string decodedPath, [NotNullWhen(true)] out string? title)
        {
            title = null;
            if (!decodedPath.StartsWith("index.php?", StringComparison.OrdinalIgnoreCase))
                return false;

            var query = decodedPath["index.php?".Length..];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                if (!kv[0].Equals("title", StringComparison.OrdinalIgnoreCase)) continue;
                title = HttpUtility.UrlDecode(kv[1]);
                return !string.IsNullOrEmpty(title);
            }

            return false;
        }

        private async Task CreatePageAsync(string canonicalFileName, LinkedTitleModel linkedTitleModel, TitleModel valueTitleModel)
        {
            Interlocked.Increment(ref _taskCount);
            try
            {
                await Task.Yield();
                var absolutePath = Path.Combine(FileDirectory, "sources", $"{canonicalFileName}.md");

                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

                await using var writer = new StreamWriter(File.Create(absolutePath));

                var sourceComment = linkedTitleModel.HtmlFilePath is null
                    ? "<!-- source html: (missing from dump; stub from titles.json) -->"
                    : $"<!-- source html: {linkedTitleModel.HtmlFilePath} -->";

                await writer.WriteLineAsync(
                    $"""
                     # {valueTitleModel.Title}

                     {sourceComment}

                     {valueTitleModel.Extract}

                     """
                );

                if (linkedTitleModel.HtmlFilePath is not null)
                {
                    var htmlDocument = new HtmlDocument();
                    htmlDocument.Load(linkedTitleModel.HtmlFilePath);

                    var parserOutput =
                        htmlDocument.DocumentNode.Descendants("div").First(node => node.HasClass("mw-parser-output"));

                    await ParseChildrenAsync(parserOutput, writer, 0);
                }
                else
                {
                    await writer.WriteLineAsync("> 静态站源中未找到该词条的 HTML，本页仅包含 titles.json 中的摘要。");
                    await writer.WriteLineAsync();
                }

                await writer.WriteLineAsync();
                await writer.WriteLineAsync(FileFooter);
            }
            catch (Exception e)
            {
                BuildLog.AddPageError(canonicalFileName, e.ToString());
            }
            Interlocked.Decrement(ref _taskCount);
            Interlocked.Increment(ref _finishedTaskCount);
        }


        private enum Type
        {
            Normal,
            UnorderedList,
            OrderedList,
        }

        private async Task ParseFootnotesAsync(HtmlNode footnotes, StreamWriter writer)
        {
            // Prefer Elements/Descendants over NextSibling — whitespace #text nodes between <li>s
            // previously aborted the loop after the first footnote definition.
            foreach (var li in footnotes.Descendants("li").Where(n => !string.IsNullOrEmpty(n.Id)))
            {
                var referenceText = li.ChildNodes.FirstOrDefault(child => child.HasClass("reference-text"))
                                    ?? li.Descendants().FirstOrDefault(child => child.HasClass("reference-text"));
                if (referenceText is null) continue;

                await writer.WriteAsync($"[^{li.Id}]: ");
                await ParseChildrenAsync(referenceText, writer, 0);
                await writer.WriteLineAsync();
            }
        }

        private async Task ParseSimpleWorkAsync(HtmlNode work, StreamWriter writer)
        {
            // Cover thumbnails are image-only links; title + props carry the useful content.
            var titleNode = work.Descendants("div").FirstOrDefault(d => d.HasClass("simple_work-title"));
            var titleLink = titleNode?.Descendants("a").FirstOrDefault();
            var titleText = HttpUtility.HtmlDecode(
                titleLink?.InnerText.Trim()
                ?? titleNode?.InnerText.Trim()
                ?? string.Empty
            );
            if (string.IsNullOrWhiteSpace(titleText))
                return;

            await writer.WriteAsync("- ");
            var titleHref = titleLink?.GetAttributeValue("href", null);
            if (!string.IsNullOrEmpty(titleHref) && TryCreateLink(titleHref, false, out var titleMd))
                await writer.WriteAsync($"**[{EscapeMarkdownLinkText(titleText)}]({EscapeMarkdownLinkDestination(titleMd)})**");
            else
                await writer.WriteAsync($"**{EscapeMarkdownLinkText(titleText)}**");

            var props = work.Descendants("div")
                .Where(d => d.HasClass("simple_work-prop"))
                .Select(d => HttpUtility.HtmlDecode(d.InnerText.Trim()))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            if (props.Count > 0)
            {
                await writer.WriteAsync(" — ");
                await writer.WriteAsync(string.Join("；", props));
            }

            await writer.WriteLineAsync();
        }

        private async Task ParseCharaListAsync(HtmlNode charaList, StreamWriter writer)
        {
            // Interactive filter UI does not translate to Markdown; emit a readable table instead.
            var items = charaList.Descendants("div")
                .Where(node => node.HasClass("chara-item") && node.GetAttributeValue("data-chara", null) != null)
                .ToList();

            await writer.WriteLineAsync();
            await writer.WriteLineAsync("| 姓名 | 日文名 | 英文名 | 别名 | 初登场作品 | 登场次数 | 分类 | 介绍 |");
            await writer.WriteLineAsync("| --- | --- | --- | --- | --- | --- | --- | --- |");

            foreach (var item in items)
            {
                var cnNameNode = item.Descendants("a").FirstOrDefault(a => a.HasClass("chara-cnname"))
                                 ?? item.Descendants("div")
                                     .FirstOrDefault(d => d.HasClass("chara-linkbox"))
                                     ?.Descendants("a")
                                     .FirstOrDefault();

                var nameText = HttpUtility.HtmlDecode(
                    cnNameNode?.InnerText.Trim()
                    ?? item.GetAttributeValue("data-chara", string.Empty)
                );
                var nameCell = nameText;
                var nameHref = cnNameNode?.GetAttributeValue("href", null);
                if (!string.IsNullOrEmpty(nameHref) && TryCreateLink(nameHref, false, out var nameLink))
                    nameCell = $"[{EscapeMarkdownTableCell(nameText)}]({EscapeMarkdownLinkDestination(nameLink)})";
                else
                    nameCell = EscapeMarkdownTableCell(nameText);

                var jp = EscapeMarkdownTableCell(TextOfClass(item, "chara-jpname"));
                var en = EscapeMarkdownTableCell(TextOfClass(item, "chara-enname"));
                var nick = EscapeMarkdownTableCell(
                    NonEmpty(
                        TextOfClass(item, "chara-nickname"),
                        item.GetAttributeValue("data-nickname", string.Empty)
                    )
                );

                var firstNode = item.Descendants("div").FirstOrDefault(d => d.HasClass("chara-first"));
                var firstLinkNode = firstNode?.Descendants("a").FirstOrDefault();
                var firstText = HttpUtility.HtmlDecode(
                    firstLinkNode?.InnerText.Trim()
                    ?? firstNode?.InnerText.Trim()
                    ?? string.Empty
                );
                var firstCell = EscapeMarkdownTableCell(firstText);
                var firstHref = firstLinkNode?.GetAttributeValue("href", null);
                if (!string.IsNullOrEmpty(firstHref) && TryCreateLink(firstHref, false, out var firstLink))
                    firstCell = $"[{EscapeMarkdownTableCell(firstText)}]({EscapeMarkdownLinkDestination(firstLink)})";

                var showCount = EscapeMarkdownTableCell(
                    NonEmpty(
                        TextOfClass(item, "chara-showcount"),
                        item.GetAttributeValue("data-showcount", string.Empty)
                    )
                );
                var tag = EscapeMarkdownTableCell(item.GetAttributeValue("data-tag", string.Empty));
                var desc = EscapeMarkdownTableCell(TextOfClass(item, "chara-desc"));

                await writer.WriteLineAsync(
                    $"| {nameCell} | {jp} | {en} | {nick} | {firstCell} | {showCount} | {tag} | {desc} |"
                );
            }

            await writer.WriteLineAsync();
        }

        private static string TextOfClass(HtmlNode root, string className)
        {
            var node = root.Descendants().FirstOrDefault(n => n.HasClass(className));
            return HttpUtility.HtmlDecode(node?.InnerText.Trim() ?? string.Empty);
        }

        private static string NonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static string EscapeMarkdownTableCell(string value)
        {
            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("|", "\\|")
                .Trim();
        }

        private static string EscapeMarkdownLinkText(string value)
        {
            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Trim();
        }

        private static string EscapeMarkdownLinkDestination(string value)
        {
            // ')' terminates inline link destinations; keep fragments usable.
            var hash = value.IndexOf('#');
            if (hash < 0)
                return value.Replace(")", "%29").Replace(" ", "%20");

            var path = value[..hash].Replace(")", "%29").Replace(" ", "%20");
            var fragment = value[(hash + 1)..].Replace(")", "%29").Replace(" ", "%20");
            return $"{path}#{fragment}";
        }
        
        private async Task ParseChildrenAsync(HtmlNode divNode, StreamWriter writer, int level, Type type = Type.Normal, bool disableLinkBreak = false)
        {
            var count = 0;
            foreach (var childNode in divNode.ChildNodes)
            {
                switch (childNode.Name)
                {           
                    case "#comment":
                    case "input":
                    case "label":
                    case "style":
                    case "header":
                    case "script":
                    case "link":
                    case "embed":
                    case "form":
                    case "button":
                    case "select":
                    case "map":
                        break;    
                    case "iframe":
                    case "img":
                    case "audio":
                    case "ruby":
                    case "svg":
                        // OuterHtml often contains newlines; never break open emphasis/headings.
                        var rawHtml = childNode.OuterHtml;
                        if (disableLinkBreak)
                        {
                            rawHtml = WhitespaceCollapseRegex().Replace(rawHtml, " ").Trim();
                            await writer.WriteAsync(rawHtml);
                        }
                        else
                        {
                            await writer.WriteLineAsync(rawHtml);
                        }
                        break;
                    case "blockquote":
                        // Recurse so wiki hrefs become local markdown links (OuterHtml left them raw).
                        await writer.WriteAsync("<blockquote>");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        await writer.WriteLineAsync("</blockquote>");
                        break;
                    case "div":
                        if (childNode.HasClass("mw-references-wrap"))
                        {
                            await ParseFootnotesAsync(childNode, writer);
                            break;
                        }

                        if (childNode.HasClass("chara-list") || childNode.Id == "chara-list")
                        {
                            await ParseCharaListAsync(childNode, writer);
                            break;
                        }

                        if (childNode.HasClass("simple_work"))
                        {
                            await ParseSimpleWorkAsync(childNode, writer);
                            break;
                        }

                        // Skip TemplateStyles / MW dedupe shells with no readable content.
                        if (childNode.HasClass("mw-heading") == false &&
                            !childNode.Descendants().Any(n =>
                                n.Name is "a" or "img" or "b" or "i" or "strong" or "em" or "ul" or "ol" or "table" or "p" or "li"
                                || (n.Name == "#text" && !string.IsNullOrWhiteSpace(n.InnerText))))
                            break;

                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        break;
                    case "span":
                    case "section":
                    case "article":
                    case "kbd":
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        break;
                    case "#text":
                    {
                        var text = childNode.InnerText;
                        if (text.Length == 0) break;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            // Keep inline spaces; drop indentation/newlines copied from HTML source.
                            if (!text.Contains('\n') && !text.Contains('\r'))
                                await writer.WriteAsync(' ');
                            break;
                        }

                        // Decode twice: some MediaWiki text nodes still contain &amp; / &#…; entities.
                        text = HttpUtility.HtmlDecode(HttpUtility.HtmlDecode(text));
                        // Emphasis/headings cannot contain raw newlines or Markdown markers break.
                        if (disableLinkBreak)
                            text = WhitespaceCollapseRegex().Replace(text, " ");

                        await writer.WriteAsync(text);
                        break;
                    }
                    case "br":
                        if(disableLinkBreak) break;
                        await writer.WriteLineAsync("  ");
                        break;
                    case "a":
                        var link = childNode.GetAttributeValue("href", null);

                        if (link is null) break;

                        var anchorText = EscapeMarkdownLinkText(
                            HttpUtility.HtmlDecode(HttpUtility.HtmlDecode(childNode.InnerText)));
                        if (string.IsNullOrWhiteSpace(anchorText))
                        {
                            // Image-only / icon anchors: emit the image, avoid empty [](url).
                            var img = childNode.Descendants("img").FirstOrDefault();
                            if (img is not null)
                            {
                                var imgHtml = WhitespaceCollapseRegex().Replace(img.OuterHtml, " ").Trim();
                                if (disableLinkBreak)
                                {
                                    // Keep gallery thumbs from gluing as "imgcaption- imgcaption".
                                    await writer.WriteAsync(imgHtml);
                                    await writer.WriteAsync(' ');
                                }
                                else
                                {
                                    await writer.WriteLineAsync(imgHtml);
                                }

                                break;
                            }

                            var titleAttr = childNode.GetAttributeValue("title", null);
                            if (string.IsNullOrWhiteSpace(titleAttr))
                                break;
                            anchorText = EscapeMarkdownLinkText(
                                HttpUtility.HtmlDecode(HttpUtility.HtmlDecode(titleAttr)));
                        }

                        if (!TryCreateLink(link, false, out var matchedLink))
                            await writer.WriteAsync($"{anchorText} (未找到链接)");
                        else
                            await writer.WriteAsync($"[{anchorText}]({EscapeMarkdownLinkDestination(matchedLink)})");
                        break;
                    case "hr":
                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync("---");
                        await writer.WriteLineAsync();
                        break;
                    case "code":
                    case "tt":
                        await writer.WriteAsync("`");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteAsync("`");
                        break;
                    case "pre":
                        await writer.WriteLineAsync();
                        await writer.WriteLineAsync("```");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync("```");
                        await writer.WriteLineAsync();
                        break;         
                    case "u":
                        await writer.WriteAsync("<u>");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        await writer.WriteAsync("</u>");
                        break;
                    case "b":
                    case "strong":
                        // <br> inside <b> must not split ** across lines or Markdown bold breaks.
                        await writer.WriteAsync("**");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak: true);
                        await writer.WriteAsync("**");
                        if (childNode.Descendants("br").Any())
                            await writer.WriteLineAsync("  ");
                        break;
                    case "i":
                    case "em":
                        await writer.WriteAsync("*");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak: true);
                        await writer.WriteAsync("*");
                        if (childNode.Descendants("br").Any())
                            await writer.WriteLineAsync("  ");
                        break;
                    case "s":
                    case "strike":
                    case "del":
                        await writer.WriteAsync(" ~~");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak: true);
                        await writer.WriteAsync("~~ ");
                        break;
                    case "center":
                        await writer.WriteAsync("<center>");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        await writer.WriteAsync("</center>");
                        break;
                    case "sub":
                        await writer.WriteAsync("<sub>");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        await writer.WriteAsync("</sub>");
                        break;
                    case "big":
                        if (!childNode.Descendants().Any(n =>
                                n.Name is "a" or "img" or "b" or "i" or "strong" or "em"
                                || (n.Name == "#text" && !string.IsNullOrWhiteSpace(n.InnerText))))
                            break;
                        await writer.WriteAsync("<big>");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak: true);
                        await writer.WriteAsync("</big>");
                        break;
                    case "small":
                        await writer.WriteAsync("<small>");
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        await writer.WriteAsync("</small>");
                        break;
                    case "font":
                        await writer.WriteAsync(new string(childNode.OuterHtml.TakeWhile(c => c != '>').Append('>').ToArray()));
                        await ParseChildrenAsync(childNode, writer, level, type, disableLinkBreak);
                        await writer.WriteAsync("</font>");
                        break;
                    case "p":
                    {
                        var nestedCharaList = childNode.Descendants("div")
                            .FirstOrDefault(d => d.HasClass("chara-list") || d.Id == "chara-list");
                        if (nestedCharaList is not null)
                        {
                            await ParseCharaListAsync(nestedCharaList, writer);
                            break;
                        }

                        // Vue/style wrappers often leave empty <p> shells after HTML repair.
                        if (!childNode.Descendants().Any(n =>
                                n.Name is "a" or "img" or "b" or "i" or "strong" or "em" or "ul" or "ol" or "table"
                                || (n.Name == "#text" && !string.IsNullOrWhiteSpace(n.InnerText))))
                            break;

                        await writer.WriteLineAsync("  ");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteLineAsync("  ");
                        break;
                    }
                    case "h1":
                        await writer.WriteLineAsync();
                        await writer.WriteAsync("# ");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    case "h2":
                    {
                        // Skip headings that only introduce dropped chrome (footnotes heading / navboxes).
                        var headingText = HttpUtility.HtmlDecode(childNode.InnerText).Trim();
                        if (headingText is "注释" or "导航" or "词条导航")
                            break;

                        await writer.WriteLineAsync();
                        await writer.WriteAsync("## ");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    }
                    case "h3":
                        await writer.WriteLineAsync();
                        await writer.WriteAsync("### ");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    case "h4":
                        await writer.WriteLineAsync();
                        await writer.WriteAsync("#### ");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    case "h5":
                        await writer.WriteLineAsync();
                        await writer.WriteAsync("##### ");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    case "h6":
                        await writer.WriteLineAsync();
                        await writer.WriteAsync("###### ");
                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    case "ul":
                        // Nested lists often follow parent <li> text with no intervening whitespace.
                        await writer.WriteLineAsync();
                        await ParseChildrenAsync(childNode, writer, level + 1, Type.UnorderedList);
                        await writer.WriteLineAsync();
                        break;
                    case "ol":
                        await writer.WriteLineAsync();
                        await ParseChildrenAsync(childNode, writer, level + 1, Type.OrderedList);
                        await writer.WriteLineAsync();
                        break;
                    case "li":
                        await writer.WriteAsync(new string(' ', Math.Max(level - 1, 0) * 2));
                        if (type is Type.OrderedList)
                        {
                            await writer.WriteAsync($"{++count}. ");
                        }
                        else
                        {
                            await writer.WriteAsync("- ");
                        }

                        await ParseChildrenAsync(childNode, writer, level, disableLinkBreak: true);
                        await writer.WriteLineAsync();
                        break;
                    case "sup":
                        var aNode = childNode.Descendants("a").FirstOrDefault();
                        if (aNode is not null)
                        {
                            var hrefValue = aNode.GetAttributeValue("href", null);

                            if (hrefValue.StartsWith('#'))
                            {
                                await writer.WriteAsync($"[^{hrefValue[1..]}]");
                                break;
                            }

                            if (!TryCreateLink(hrefValue, false, out matchedLink))
                                await writer.WriteAsync($"{EscapeMarkdownLinkText(HttpUtility.HtmlDecode(childNode.InnerText))} (未找到链接)");
                            else
                                await writer.WriteAsync($"[{EscapeMarkdownLinkText(HttpUtility.HtmlDecode(childNode.InnerText))}]({EscapeMarkdownLinkDestination(matchedLink)})");
                        }
                        else
                        {
                            await writer.WriteLineAsync(HttpUtility.HtmlDecode(childNode.InnerText));
                        }
                   
                        break;
                    case "dl":
                        foreach (var subNode in childNode.ChildNodes)
                        {
                            switch (subNode.Name)
                            {
                                case "dt":
                                    await writer.WriteLineAsync(HttpUtility.HtmlDecode(subNode.InnerText));
                                    break;
                                case "dd":
                                    await writer.WriteAsync(": ");
                                    await ParseChildrenAsync(subNode, writer, 0, type);
                                    await writer.WriteLineAsync();
                                    break;
                            }
                        }
                        break;
                    case "table":
                        // Navboxes / collapsible template nav are often wrapped in a classless outer <table>.
                        if (IsNavigationTable(childNode))
                            break;

                        // Drop MediaWiki TemplateStyles / prefetch / track-search icon junk.
                        foreach (var junk in childNode.Descendants()
                                     .Where(n =>
                                         n.Name is "style" or "link" or "script" ||
                                         n.HasClass("thcsearchlinks"))
                                     .ToList())
                            junk.Remove();

                        // Nested work cards sometimes sit inside/beside table shells.
                        var nestedWorks = childNode.Descendants("div")
                            .Where(d => d.HasClass("simple_work"))
                            .ToList();
                        if (nestedWorks.Count > 0)
                        {
                            foreach (var work in nestedWorks)
                                await ParseSimpleWorkAsync(work, writer);
                            break;
                        }

                        if (!childNode.Descendants("tr").Any() &&
                            string.IsNullOrWhiteSpace(HttpUtility.HtmlDecode(childNode.InnerText)))
                            break;

                        ReplaceInlineHtmlHRef(childNode);
                        NormalizeTableCitations(childNode);
                        DecodeHtmlTextNodes(childNode);

                        await writer.WriteAsync("\n<table>");
                        await writer.WriteAsync(childNode.InnerHtml);
                        await writer.WriteLineAsync("</table>\n");
                        break;
                    default:
                        await writer.WriteAsync($"<unsupported html={childNode.Name}>");
                        BuildLog.AddUnsupportedTag(childNode.Name);
                        break;
                }
            }
        }

        private static bool IsNavigationTable(HtmlNode table)
        {
            static bool LooksLikeNav(HtmlNode n) =>
                n.HasClass("navbox") ||
                n.HasClass("navbox-title") ||
                n.HasClass("navigation-not-searchable") ||
                (n.HasClass("mw-collapsible") && n.HasClass("nowraplinks"));

            return LooksLikeNav(table) || table.Descendants().Any(LooksLikeNav);
        }

        private void ReplaceInlineHtmlHRef(HtmlNode node)
        {
            foreach (var aNode in node.Descendants("a").ToList())
            {
                var hrefValue = aNode.GetAttributeValue("href", null);
                if (hrefValue == null) continue;

                var decodedHref = HttpUtility.HtmlDecode(HttpUtility.UrlDecode(hrefValue));
                // SMW ask / special upload / template edit URLs are not useful in Markdown.
                if (decodedHref.Contains("特殊:", StringComparison.Ordinal) ||
                    decodedHref.Contains("Special:", StringComparison.OrdinalIgnoreCase) ||
                    decodedHref.Contains("action=edit", StringComparison.OrdinalIgnoreCase))
                {
                    aNode.Attributes.Remove("href");
                    continue;
                }

                if (TryCreateLink(hrefValue, false, out var link))
                {
                    aNode.SetAttributeValue("href", link);
                    continue;
                }

                // Redlinks / unresolved wiki hrefs: keep label text, drop dead href noise.
                aNode.Attributes.Remove("href");
            }
        }

        private static void NormalizeTableCitations(HtmlNode table)
        {
            foreach (var sup in table.Descendants("sup").ToList())
            {
                var aNode = sup.Descendants("a").FirstOrDefault();
                var href = aNode?.GetAttributeValue("href", null);
                if (href is null || !href.StartsWith("#cite_", StringComparison.Ordinal))
                    continue;

                var replacement = HtmlNode.CreateNode($"<span>[^{href[1..]}]</span>");
                sup.ParentNode?.ReplaceChild(replacement, sup);
            }
        }

        private static void DecodeHtmlTextNodes(HtmlNode root)
        {
            foreach (var node in root.DescendantsAndSelf())
            {
                if (node.NodeType != HtmlNodeType.Text) continue;
                var textNode = (HtmlTextNode)node;
                textNode.Text = HttpUtility.HtmlDecode(HttpUtility.HtmlDecode(textNode.Text));
            }
        }
    }
}