using System.Collections.Concurrent;
using System.Text;

internal partial class Program
{
    private sealed class BuildLog
    {
        private readonly ConcurrentDictionary<string, byte> _missingHtml = [];
        private readonly ConcurrentDictionary<string, byte> _missingRedirectTargets = [];
        private readonly ConcurrentDictionary<string, byte> _unresolvedLinks = [];
        private readonly ConcurrentDictionary<string, string> _pageErrors = [];
        private readonly ConcurrentDictionary<string, byte> _unsupportedTags = [];

        public void AddMissingHtml(string message) => _missingHtml.TryAdd(message, 0);

        public void AddMissingRedirectTarget(string message) => _missingRedirectTargets.TryAdd(message, 0);

        public void AddUnresolvedLink(string message) => _unresolvedLinks.TryAdd(message, 0);

        public void AddPageError(string page, string error) => _pageErrors.TryAdd(page, error);

        public void AddUnsupportedTag(string tag) => _unsupportedTags.TryAdd(tag, 0);

        public void PrintToConsole()
        {
            const int sampleLimit = 20;

            Console.WriteLine();
            Console.WriteLine("=== Build Log Summary ===");
            Console.WriteLine($"Missing HTML sources: {_missingHtml.Count}");
            Console.WriteLine($"Missing redirect targets: {_missingRedirectTargets.Count}");
            Console.WriteLine($"Unresolved links: {_unresolvedLinks.Count}");
            Console.WriteLine($"Page build errors: {_pageErrors.Count}");
            Console.WriteLine($"Unsupported tags: {_unsupportedTags.Count}");

            PrintSample("Missing HTML sources (sample)", _missingHtml.Keys, sampleLimit);
            PrintSample("Missing redirect targets (sample)", _missingRedirectTargets.Keys, sampleLimit);
            PrintSample("Unresolved links (sample)", _unresolvedLinks.Keys, sampleLimit);

            foreach (var log in _pageErrors.OrderBy(x => x.Key, StringComparer.Ordinal).Take(sampleLimit))
                Console.WriteLine($"Error when creating page [{log.Key}] {log.Value}");

            if (!_unsupportedTags.IsEmpty)
                Console.WriteLine($"Unsupported Tags: {string.Join(", ", _unsupportedTags.Keys.OrderBy(x => x, StringComparer.Ordinal))}");
        }

        private static void PrintSample(string title, IEnumerable<string> values, int limit)
        {
            var list = values.OrderBy(x => x, StringComparer.Ordinal).Take(limit + 1).ToList();
            if (list.Count == 0) return;
            Console.WriteLine($"--- {title} ---");
            foreach (var item in list.Take(limit))
                Console.WriteLine(item);
            if (list.Count > limit)
                Console.WriteLine("...");
        }

        public async Task WriteToFileAsync(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"THBWiki Markdown Builder log — {DateTimeOffset.Now:O}");
            sb.AppendLine();

            AppendSection(sb, "Missing HTML sources", _missingHtml.Keys.OrderBy(x => x, StringComparer.Ordinal));
            AppendSection(sb, "Missing redirect targets", _missingRedirectTargets.Keys.OrderBy(x => x, StringComparer.Ordinal));
            AppendSection(sb, "Unresolved links", _unresolvedLinks.Keys.OrderBy(x => x, StringComparer.Ordinal));

            sb.AppendLine("## Page build errors");
            sb.AppendLine($"Count: {_pageErrors.Count}");
            foreach (var log in _pageErrors.OrderBy(x => x.Key, StringComparer.Ordinal))
                sb.AppendLine($"[{log.Key}] {log.Value}");
            sb.AppendLine();

            sb.AppendLine("## Unsupported tags");
            sb.AppendLine($"Count: {_unsupportedTags.Count}");
            if (!_unsupportedTags.IsEmpty)
                sb.AppendLine(string.Join(", ", _unsupportedTags.Keys.OrderBy(x => x, StringComparer.Ordinal)));
            sb.AppendLine();

            await File.WriteAllTextAsync(path, sb.ToString());
        }

        private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> lines)
        {
            var list = lines as IList<string> ?? lines.ToList();
            sb.AppendLine($"## {title}");
            sb.AppendLine($"Count: {list.Count}");
            foreach (var line in list)
                sb.AppendLine(line);
            sb.AppendLine();
        }
    }
}
