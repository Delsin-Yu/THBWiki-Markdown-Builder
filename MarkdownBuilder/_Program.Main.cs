using System.Collections.Concurrent;

internal partial class Program
{
    public static async Task Main(string[] args)
    {
        if (!ParseArgs(args, out var firstRun, out var outputDirArg, out var onlyPage))
            return;

        CreatePaths(
            outputDirArg,
            out var archiveZipPath,
            out var mainTarPath,
            out var fileTarPath,
            out var otherTarPath,
            out var tempDir,
            out var tempArchiveDir,
            out var tempMainDir,
            out var tempFileDir,
            out var tempOtherDir,
            out var markdownDir
        );

        if (!File.Exists(archiveZipPath)) throw new FileNotFoundException(archiveZipPath);
        if (!File.Exists(mainTarPath)) throw new FileNotFoundException(mainTarPath);
        if (!File.Exists(fileTarPath)) throw new FileNotFoundException(fileTarPath);
        if (!File.Exists(otherTarPath)) throw new FileNotFoundException(otherTarPath);

        Directory.CreateDirectory(markdownDir);

        var buildLog = new BuildLog();
        var paths = new ConcurrentBag<string>();

        var htmlAlreadyExtracted = Directory.Exists(tempDir) &&
                                   Directory.EnumerateFiles(tempDir, "*.html", SearchOption.AllDirectories).Any();
        var shouldExtract = firstRun || !htmlAlreadyExtracted;

        if (shouldExtract)
        {
            if (!firstRun)
                Console.WriteLine("Extract cache missing HTML; running extract as if --first-run was set.");

            await ExtractZipAndTarsAsync(
                archiveZipPath,
                mainTarPath,
                fileTarPath,
                otherTarPath,
                tempDir,
                tempArchiveDir,
                tempMainDir,
                tempFileDir,
                tempOtherDir
            );

            await ExtractBrotliArchivesAsync(
                tempMainDir,
                tempFileDir,
                tempOtherDir,
                paths
            );
        }
        else
        {
            Directory.GetFiles(tempDir, "*.html", SearchOption.AllDirectories).AsParallel().ForAll(paths.Add);
        }

        Console.WriteLine($"Markdown output: {markdownDir}");
        Console.WriteLine($"Extract cache: {tempDir}");

        var titleDictionary = await LinkWikiStructureAsync(tempArchiveDir, paths, buildLog);

        if (!string.IsNullOrWhiteSpace(onlyPage))
        {
            await BuildSinglePageAsync(onlyPage, titleDictionary, markdownDir, buildLog);
        }
        else
        {
            var thbWikiEmptyPage = titleDictionary.Values.First(title => title.LinkedTitleModel.TitleModel.Id == 3288);
            var topPageHtml = thbWikiEmptyPage.LinkedTitleModel.HtmlFilePath
                              ?? throw new FileNotFoundException("Missing HTML for THBWiki navigation page (id 3288).");

            var topPages = ParseTopPage(topPageHtml);
            await BuildPagesAsync(topPages, titleDictionary, markdownDir, buildLog);
        }

        var logPath = Path.Combine(markdownDir, "build-logs.log");
        await buildLog.WriteToFileAsync(logPath);
        buildLog.PrintToConsole();

        Console.WriteLine($"Build log written to: {logPath}");
        Console.WriteLine("Finish");
    }
}
