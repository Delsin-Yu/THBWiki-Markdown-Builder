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

        
        
        // ↓↓↓↓↓↓↓↓ Comment the code bellow after the first run ↓↓↓↓↓↓↓↓
        
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

        // ↑↑↑↑↑↑↑↑ Comment the code above after the first run ↑↑↑↑↑↑↑↑
        
        
        
        // ↓↓↓↓↓↓↓↓ Uncomment the code bellow after the first run ↓↓↓↓↓↓↓↓
        
        // Directory.GetFiles(tempDir, "*.html", SearchOption.AllDirectories).AsParallel().ForAll(paths.Add);
        
        // ↑↑↑↑↑↑↑↑ Uncomment the code above after the first run ↑↑↑↑↑↑↑↑

        
        
        var (linkedWikiStructure, titleDictionary) = await LinkWikiStructureAsync(tempArchiveDir, paths);
        
        var thbWikiNamespace = linkedWikiStructure.Keys.First(ns => ns.Id == 4);
        var thbWikiEmptyPage = linkedWikiStructure[thbWikiNamespace].First(title => title.TitleModel.Id == 3288);

        var topPages = ParseTopPage(thbWikiEmptyPage.HtmlFilePath);

        await BuildPagesAsync(topPages, titleDictionary, markdownDir);


        Console.WriteLine("Finish");
    }
}