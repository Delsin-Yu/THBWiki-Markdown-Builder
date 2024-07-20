using System.Text;
using System.Web;
using HtmlAgilityPack;

internal partial class Program
{
    private record HyperLinkNode(string DisplayName, string? Link)
    {
        public List<HyperLinkNode> Children { get; } = [];

        public override string ToString()
        {
            var builder = new StringBuilder();
            ToStringCore(builder, 0);
            return builder.ToString();
        }

        private void ToStringCore(StringBuilder builder, int indent)
        {
            builder
                .Append('-', indent * 2)
                .Append(DisplayName)
                .Append(", href=")
                .AppendLine(Link);

            foreach (var child in Children) child.ToStringCore(builder, indent + 1);
        }
    }

    private static HyperLinkNode[] ParseTopPage(string pagePath)
    {
        using var fileStream = File.OpenRead(pagePath);
        var document = new HtmlDocument();
        document.Load(fileStream);
        HtmlNode[] nodes =
        [
            document.GetElementbyId("p-常用"),
            document.GetElementbyId("p-官方作品"),
            document.GetElementbyId("p-二次创作与活动"),
            document.GetElementbyId("p-THB相关项目"),
        ];

        return nodes.Select(
            node => CreateTree(
                node.ChildNodes.FindFirst("h3")
                    .ChildNodes.FindFirst("span")
                    .InnerText,
                node.ChildNodes.FindFirst("div")
                    .ChildNodes.FindFirst("ul")
            )
        ).ToArray();
    }

    private static HyperLinkNode CreateTree(string name, HtmlNode ul)
    {
        var root = new HyperLinkNode(name, null);
        ReadUl(ul, root);
        return root;

        static void ReadUl(HtmlNode node, HyperLinkNode parent)
        {
            foreach (var li in node.ChildNodes.Where(node => node.Name == "li"))
            {
                var liFirstChild = li.FirstChild;
                var hrefLink = liFirstChild.Attributes.FirstOrDefault(attribute => attribute.Name == "href")?.Value;

                if (hrefLink is not null && hrefLink.StartsWith('/'))
                    hrefLink = HttpUtility.UrlDecode(hrefLink)[1..];

                var child = new HyperLinkNode(
                    liFirstChild.InnerText,
                    hrefLink
                );
                parent.Children.Add(child);

                var div = li.ChildNodes.FirstOrDefault(cn => cn.Name == "div");

                if (div == null) continue;

                ReadUl(div.FirstChild, child);
            }
        }
    }
}