using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text.Encodings.Web;
using System.Web;

var root = Path.GetFullPath("./../../../../");

var sourceDir = Path.Combine(root, "THBWikiSources");
var archiveZipPath = Path.Combine(sourceDir, "archive.zip");
var mainTarPath = Path.Combine(sourceDir, "main.tar");
var fileTarPath = Path.Combine(sourceDir, "file.tar");
var otherTarPath = Path.Combine(sourceDir, "other.tar");

if(!File.Exists(archiveZipPath)) throw new FileNotFoundException(archiveZipPath);
if(!File.Exists(mainTarPath)) throw new FileNotFoundException(mainTarPath);
if(!File.Exists(fileTarPath)) throw new FileNotFoundException(fileTarPath);
if(!File.Exists(otherTarPath)) throw new FileNotFoundException(otherTarPath);

var markdownDir = Path.Combine(root, "THBWikiMarkdown");
var tempDir = Path.Combine(markdownDir, "Temp");


#region Extract Resources


var tempArchiveDir = Path.Combine(tempDir, "archive");
var tempMainDir = Path.Combine(tempDir, "main");
var tempFileDir = Path.Combine(tempDir, "file");
var tempOtherDir = Path.Combine(tempDir, "other");

if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);

Directory.CreateDirectory(tempArchiveDir);
Directory.CreateDirectory(tempMainDir);
Directory.CreateDirectory(tempFileDir);
Directory.CreateDirectory(tempOtherDir);

Console.WriteLine("Start extracting resources...");

var archiveUnzipTask = Task.Run(
    () =>
    {
        Console.WriteLine("Start extracting archive.zip...");
        ZipFile.ExtractToDirectory(archiveZipPath, tempArchiveDir);
        Console.WriteLine("Finish extracting archive.zip.");
    }
);
var mainExTarTask = Task.Run(
    async () =>
    {
        Console.WriteLine("Start extracting main.tar...");
        await TarFile.ExtractToDirectoryAsync(mainTarPath, tempMainDir, true);
        Console.WriteLine("Finish extracting main.tar.");
    }
);
var fileExTarTask = Task.Run(
    async () =>
    {
        Console.WriteLine("Start extracting file.tar...");
        await TarFile.ExtractToDirectoryAsync(fileTarPath, tempFileDir, true);
        Console.WriteLine("Finish extracting file.tar.");
    }
);
var otherExTarTask = Task.Run(
    async () =>
    {
        Console.WriteLine("Start extracting other.tar...");
        await TarFile.ExtractToDirectoryAsync(otherTarPath, tempOtherDir, true);
        Console.WriteLine("Finish extracting other.tar.");
    }
);

await Task.WhenAll(archiveUnzipTask, mainExTarTask, fileExTarTask, otherExTarTask);

Console.WriteLine("Finish extracting resources.");

#endregion

Console.WriteLine("Start extracting brotli archives");

var totalTaskCount = 0;

async Task ExtractRecursiveAsync(string path)
{
    var brotliFilePaths = Directory.GetFiles(path, "*.br", SearchOption.AllDirectories);
    Interlocked.Add(ref totalTaskCount, brotliFilePaths.Length);
    await Task.Yield();
    var tasks = brotliFilePaths.Select(
        async htmlFilePath =>
        {
            var destPath = Path.Combine(Path.GetDirectoryName(htmlFilePath)!, Path.GetFileNameWithoutExtension(htmlFilePath));
            await using var outputStream = File.Create(destPath);
            await using var fileStream = File.OpenRead(htmlFilePath);
            await using var stream = new BrotliStream(fileStream, CompressionMode.Decompress);
            await stream.CopyToAsync(outputStream);
            Interlocked.Decrement(ref totalTaskCount);
        }
    );
    await Task.WhenAll(tasks);
}

async Task Log(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        Console.WriteLine($"Remain Tasks: {totalTaskCount}");
        await Task.Delay(1000, token);
    }    
}


var tokenSource = new CancellationTokenSource();
_ = Log(tokenSource.Token);

try
{
    await Task.WhenAll(
        ExtractRecursiveAsync(tempMainDir),
        ExtractRecursiveAsync(tempFileDir),
        ExtractRecursiveAsync(tempOtherDir)
    );
}
finally
{
    tokenSource.Cancel();
}

Console.WriteLine("Finish extracting brotli archives");

Console.WriteLine("Finish");