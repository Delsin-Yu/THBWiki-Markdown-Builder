// ↓↓↓↓↓↓↓↓ Comment the code bellow after the first run ↓↓↓↓↓↓↓↓
// #define FIRST_RUN
// ↑↑↑↑↑↑↑↑ Comment the code above after the first run ↑↑↑↑↑↑↑↑

using System.Collections.Concurrent;

internal partial class Program
{
    public static async Task Main()
    {
        CreatePaths(
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

        var paths = new ConcurrentBag<string>();


#if FIRST_RUN
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
#endif


#if !FIRST_RUN
        Directory.GetFiles(tempDir, "*.html", SearchOption.AllDirectories).AsParallel().ForAll(paths.Add);
#endif


        var titleDictionary = await LinkWikiStructureAsync(tempArchiveDir, paths);
        var thbWikiEmptyPage = titleDictionary.Values.First(title => title.LinkedTitleModel.TitleModel.Id == 3288);

        var topPages = ParseTopPage(thbWikiEmptyPage.LinkedTitleModel.HtmlFilePath);

        await BuildPagesAsync(topPages, titleDictionary, markdownDir);

        Console.WriteLine("Finish");
    }
}