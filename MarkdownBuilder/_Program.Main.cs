using System.Collections.Concurrent;
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

        await ExtractZipAndTars(
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
        await ExtractBrotliArchives(
            tempMainDir,
            tempFileDir,
            tempOtherDir,
            paths
        );

        var namespaces = new List<Namespace>();
        await ParseNamespaces(tempArchiveDir, namespaces);

        Console.WriteLine("Finish");
    }
}