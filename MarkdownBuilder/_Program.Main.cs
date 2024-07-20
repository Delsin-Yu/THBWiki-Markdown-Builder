using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Web;
using System.Xml;

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
            out var tempOtherDir
        );

        if (!File.Exists(archiveZipPath)) throw new FileNotFoundException(archiveZipPath);
        if (!File.Exists(mainTarPath)) throw new FileNotFoundException(mainTarPath);
        if (!File.Exists(fileTarPath)) throw new FileNotFoundException(fileTarPath);
        if (!File.Exists(otherTarPath)) throw new FileNotFoundException(otherTarPath);

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

        var paths = new ConcurrentBag<string>();
        await ExtractBrotliArchivesAsync(
            tempMainDir,
            tempFileDir,
            tempOtherDir,
            paths
        );

        var linkedWikiStructure = await LinkWikiStructureAsync(tempArchiveDir, paths);

        var thbWikiNamespace = linkedWikiStructure.Keys.First(ns => ns.Id == 4);
        var thbWikiEmptyPage = linkedWikiStructure[thbWikiNamespace].First(title => title.TitleModel.Id == 3288);
        
        Console.WriteLine("Finish");
    }
}