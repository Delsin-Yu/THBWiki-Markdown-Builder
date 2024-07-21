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
    
    private static async Task BuildPagesAsync(
        HyperLinkNode[] topPages,
        FrozenDictionary<string, LinkedTitleModel> titleModelLookup,
        string markdownDir)
    {
        var indexBuilder = new StringBuilder("# THBWiki - Markdown\n\n");

        var absoluteMainPageLink = Path.Combine(markdownDir, "readme.md");
        const string relativeMainPageLink = $"./readme.md";
        var lookupInfo = new LookupInfo(titleModelLookup, relativeMainPageLink, markdownDir);

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

        lookupInfo.PrintError();
        lookupInfo.PrintUnsupported();

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
        FrozenDictionary<string, LinkedTitleModel> TitleDictionary,
        string MainPageLink,
        string FileDirectory)
    {
        private readonly ConcurrentBag<string> _linkErrorLog = [];
        private readonly ConcurrentDictionary<string, string> _errorLog = [];
        private readonly ConcurrentDictionary<string, byte> _unsupportedHtmlTags = [];
        private readonly ConcurrentDictionary<TitleModel, byte> _createdPages = [];

        private int _taskCount;
        private int _finishedTaskCount;

        [GeneratedRegex("""[\\/:*?""<>|]""")]
        private partial Regex GetReplaceFileNameRegex();

        public async Task WaitForFinish()
        {
            while (_taskCount > 0)
            {
                await Task.Delay(1000);
                Console.WriteLine($"Remaining Tasks: {_taskCount}, Finished Tasks: {_finishedTaskCount}");
            }
        }

        public void PrintError()
        {
            foreach (var log in _errorLog)
            {
                Console.WriteLine($"Error when creating page [{log.Key}] {log.Value}");
            }
        }

        public void PrintLinkError()
        {
            foreach (var log in _linkErrorLog)
            {
                Console.WriteLine($"Unable to find {log}");
            }
        }

        public void PrintUnsupported()
        {
            Console.WriteLine($"Unsupported Tags: {string.Join(", ", _unsupportedHtmlTags.Keys)}");
        }

        public bool TryCreateLink(string rawHref, bool isRoot, [NotNullWhen(true)] out string? link)
        {
            var decoded = HttpUtility.UrlDecode(rawHref);
            if (!decoded.StartsWith('/'))
            {
                link = decoded;
                return true;
            }
            decoded = decoded[1..];
            if (!TitleDictionary.TryGetValue(decoded, out var linkedTitleModel))
            {
                _linkErrorLog.Add($"{decoded}, {rawHref}");
                link = null;
                return false;
            }

            decoded = GetReplaceFileNameRegex().Replace(decoded, "-");

            var valueTitleModel = linkedTitleModel.TitleModel;
            if (valueTitleModel.Id == 1)
            {
                link = MainPageLink;
                return true;
            }

            var path = isRoot ? $"./sources/{decoded}.md" : $"./{decoded}.md";

            if (_createdPages.TryAdd(valueTitleModel, 0))
            {
                _ = CreatePageAsync(decoded, linkedTitleModel, valueTitleModel);
            }

            link = path;
            return true;
        }

        private async Task CreatePageAsync(string decoded, LinkedTitleModel linkedTitleModel, TitleModel valueTitleModel)
        {
            Interlocked.Increment(ref _taskCount);
            try
            {
                await Task.Yield();
                var absolutePath = $"{FileDirectory}/sources/{decoded}.md";

                var htmlDocument = new HtmlDocument();
                htmlDocument.Load(linkedTitleModel.HtmlFilePath);

                var parserOutput =
                    htmlDocument.DocumentNode.Descendants("div").First(node => node.HasClass("mw-parser-output"));

                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                await using var writer = new StreamWriter(File.OpenWrite(absolutePath));

                await writer.WriteLineAsync(
                    $"""
                     # {valueTitleModel.Title}

                     <!-- source html: {linkedTitleModel.HtmlFilePath} -->

                     {valueTitleModel.Extract}

                     """
                );

                await ParseChildrenAsync(parserOutput, writer, 0);


                await writer.WriteLineAsync(
                    FileFooter
                );
            }
            catch (Exception e)
            {
                _errorLog.TryAdd(decoded, e.ToString());
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
            var li = footnotes.Descendants("li").First();
            do
            {
                await writer.WriteAsync($"[^{li.Id}]: ");
                await ParseChildrenAsync(li.ChildNodes.First(child => child.HasClass("reference-text")), writer, 0);
                await writer.WriteLineAsync();
                li = li.NextSibling;
            } while (li is not null && li.Name == "li");
        }
        
        private async Task ParseChildrenAsync(HtmlNode divNode, StreamWriter writer, int level, Type type = Type.Normal)
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
                        break;    
                    case "iframe":
                    case "img":
                    case "audio":
                    case "ruby":
                    case "svg":
                        await writer.WriteLineAsync(childNode.OuterHtml);
                        break;
                    case "div":
                        if (childNode.HasClass("mw-references-wrap"))
                        {
                            await ParseFootnotesAsync(childNode, writer);
                            break;
                        }
                        await ParseChildrenAsync(childNode, writer, level);  break;
                    case "span":
                    case "section":
                    case "article":
                    case "kbd":
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "#text":
                        await writer.WriteAsync(childNode.InnerText);
                        break;
                    case "br":
                        await writer.WriteLineAsync("  ");
                        break;
                    case "a":
                        var link = childNode.GetAttributeValue("href", null);
                        
                        if(link is null) break;
                        
                        if (!TryCreateLink(link, false, out var matchedLink))
                            await writer.WriteAsync($"{childNode.InnerText} (未找到链接)");
                        else
                            await writer.WriteAsync($"[{childNode.InnerText}]({matchedLink})");
                        break;
                    case "hr":
                        await writer.WriteLineAsync("___");
                        break;
                    case "code":
                    case "tt":
                        await writer.WriteLineAsync("`");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteLineAsync("`");
                        break;         
                    case "pre":
                        await writer.WriteLineAsync("```");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteLineAsync("```");
                        break;         
                    case "u":
                        await writer.WriteAsync("<u>");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</u>");
                        break;
                    case "b":
                        await writer.WriteAsync(" **");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("** ");
                        break;
                    case "i":
                        await writer.WriteAsync(" *");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("* ");
                        break;
                    case "s":
                    case "strike":
                    case "del":
                        await writer.WriteAsync(" ~~");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("~~ ");
                        break;
                    case "center":
                        await writer.WriteAsync("<center>");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</center>");
                        break;
                    case "strong":
                        await writer.WriteAsync("<strong>");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</strong>");
                        break;
                    case "sub":
                        await writer.WriteAsync("<sub>");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</sub>");
                        break;
                    case "big":
                        await writer.WriteAsync("<big>");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</big>");
                        break;
                    case "small":
                        await writer.WriteAsync("<small>");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</small>");
                        break;
                    case "font":
                        await writer.WriteAsync(new string(childNode.OuterHtml.TakeWhile(c => c != '>').Append('>').ToArray()));
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteAsync("</font>");
                        break;
                    case "p":
                        await writer.WriteLineAsync("  ");
                        await ParseChildrenAsync(childNode, writer, level);
                        await writer.WriteLineAsync("  ");
                        break;
                    case "h1":
                        await writer.WriteAsync("# ");
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "h2":
                        // 不渲染 “注释”
                        if(childNode.FirstChild.Id == ".E6.B3.A8.E9.87.8A") continue;
                        await writer.WriteAsync("## ");
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "h3":
                        await writer.WriteAsync("### ");
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "h4":
                        await writer.WriteAsync("#### ");
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "h5":
                        await writer.WriteAsync("##### ");
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "h6":
                        await writer.WriteAsync("###### ");
                        await ParseChildrenAsync(childNode, writer, level);
                        break;
                    case "ul":
                        await ParseChildrenAsync(childNode, writer, level + 1, Type.UnorderedList);
                        await writer.WriteLineAsync();
                        break;
                    case "ol":
                        await ParseChildrenAsync(childNode, writer, level + 1, Type.OrderedList);
                        await writer.WriteLineAsync();
                        break;
                    case "li":
                        await writer.WriteAsync(new string(' ', (level - 1) * 2));
                        if(type is Type.OrderedList)
                        {
                            await writer.WriteAsync($"{++count} ");
                        }
                        else
                        {
                            await writer.WriteAsync("- ");
                        }
                        await ParseChildrenAsync(childNode, writer, level);
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
                                await writer.WriteAsync($"{childNode.InnerText} (未找到链接)");
                            else
                                await writer.WriteAsync($"[{childNode.InnerText}]({matchedLink})");
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
                                    await writer.WriteLineAsync(subNode.InnerText);
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
                        if(childNode.HasClass("navbox") &&
                           childNode.HasClass("navigation-not-searchable") &&
                           childNode.HasClass("nav-misc"))
                            break;
                        await writer.WriteAsync("\n<table>");
                        ReplaceInlineHtmlHRef(childNode);
                        await writer.WriteAsync(childNode.InnerHtml);
                        await writer.WriteLineAsync("</table>\n");
                        break;
                    default:
                        await writer.WriteAsync($"<unsupported html={childNode.Name}>");
                        _unsupportedHtmlTags.TryAdd(childNode.Name, 0);
                        break;
                }
            }
        }

        private void ReplaceInlineHtmlHRef(HtmlNode node)
        {
            foreach (var aNode in node.Descendants("a"))
            {
                var hrefValue = aNode.GetAttributeValue("href", null);
                if(hrefValue == null) continue;
                if (!TryCreateLink(hrefValue, false, out var link)) continue;
                aNode.SetAttributeValue("href", link);
            }
        }
    }
}