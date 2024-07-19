using System.Formats.Tar;
using System.IO.Compression;

internal partial class Program
{
    private static async Task ExtractZipAndTars(
        string archiveZipPath,
        string mainTarPath,
        string fileTarPath,
        string otherTarPath,
        string tempDir,
        string tempArchiveDir,
        string tempMainDir,
        string tempFileDir,
        string tempOtherDir)
    {
        Console.WriteLine("Start extracting resources...");

        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

        Directory.CreateDirectory(tempArchiveDir);
        Directory.CreateDirectory(tempMainDir);
        Directory.CreateDirectory(tempFileDir);
        Directory.CreateDirectory(tempOtherDir);

        var archiveUnzipTask = Task.Run(
            () =>
            {
                Console.WriteLine("    Start extracting archive.zip...");
                ZipFile.ExtractToDirectory(archiveZipPath, tempArchiveDir);
                Console.WriteLine("    Finish extracting archive.zip");
            }
        );
        var mainExTarTask = Task.Run(
            async () =>
            {
                Console.WriteLine("    Start extracting main.tar...");
                await TarFile.ExtractToDirectoryAsync(mainTarPath, tempMainDir, true);
                Console.WriteLine("    Finish extracting main.tar");
            }
        );
        var fileExTarTask = Task.Run(
            async () =>
            {
                Console.WriteLine("    Start extracting file.tar...");
                await TarFile.ExtractToDirectoryAsync(fileTarPath, tempFileDir, true);
                Console.WriteLine("    Finish extracting file.tar");
            }
        );
        var otherExTarTask = Task.Run(
            async () =>
            {
                Console.WriteLine("    Start extracting other.tar...");
                await TarFile.ExtractToDirectoryAsync(otherTarPath, tempOtherDir, true);
                Console.WriteLine("    Finish extracting other.tar");
            }
        );

        await Task.WhenAll(archiveUnzipTask, mainExTarTask, fileExTarTask, otherExTarTask);

        Console.WriteLine("Finish extracting resources.");
    }
}