using System.Collections.Concurrent;
using System.IO.Compression;

internal partial class Program
{
    private static async Task ExtractBrotliArchivesAsync(
        string tempMainDir,
        string tempFileDir,
        string tempOtherDir,
        ConcurrentBag<string> htmlFilePaths)
    {
        Console.WriteLine("Start extracting brotli archives");

        var totalTaskCount = 0;
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
            await tokenSource.CancelAsync();
        }

        Console.WriteLine("Finish extracting brotli archives");
        return;

        async Task Log(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Console.WriteLine($"Remain Tasks: {totalTaskCount}");
                await Task.Delay(1000, token);
            }
        }

        async Task ExtractRecursiveAsync(string path)
        {
            var brotliFilePaths = Directory.GetFiles(path, "*.br", SearchOption.AllDirectories);
            Interlocked.Add(ref totalTaskCount, brotliFilePaths.Length);
            await Task.Yield();
            var tasks = brotliFilePaths.Select(
                async htmlFilePath =>
                {
                    var destPath = Path.Combine(
                        Path.GetDirectoryName(htmlFilePath)!,
                        Path.GetFileNameWithoutExtension(htmlFilePath)
                    );
                    htmlFilePaths.Add(destPath);
                    await using var outputStream = File.Create(destPath);
                    await using var fileStream = File.OpenRead(htmlFilePath);
                    await using var stream = new BrotliStream(fileStream, CompressionMode.Decompress);
                    await stream.CopyToAsync(outputStream, tokenSource.Token);
                    Interlocked.Decrement(ref totalTaskCount);
                }
            );
            await Task.WhenAll(tasks);
        }
    }
}