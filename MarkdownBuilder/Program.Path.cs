internal partial class Program
{
    private static void CreatePaths(
        string? outputDirArg,
        out string archiveZipPath,
        out string mainTarPath,
        out string fileTarPath,
        out string otherTarPath,
        out string tempDir,
        out string tempArchiveDir,
        out string tempMainDir,
        out string tempFileDir,
        out string tempOtherDir,
        out string markdownDir)
    {
        // bin/{Configuration}/net8.0 → repository root
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var sourceDir = Path.Combine(root, "THBWikiSources");
        archiveZipPath = Path.Combine(sourceDir, "archive.zip");
        mainTarPath = Path.Combine(sourceDir, "main.tar");
        fileTarPath = Path.Combine(sourceDir, "file.tar");
        otherTarPath = Path.Combine(sourceDir, "other.tar");

        // Heavy extract cache stays in the repo so subsequent runs can skip re-extract.
        var extractRoot = Path.Combine(root, "THBWikiMarkdown");
        tempDir = Path.Combine(extractRoot, "Temp");
        tempArchiveDir = Path.Combine(tempDir, "archive");
        tempMainDir = Path.Combine(tempDir, "main");
        tempFileDir = Path.Combine(tempDir, "file");
        tempOtherDir = Path.Combine(tempDir, "other");

        markdownDir = string.IsNullOrWhiteSpace(outputDirArg)
            ? Path.Combine(Path.GetTempPath(), "THBWikiMarkdown")
            : Path.GetFullPath(outputDirArg);
    }

    private static bool ParseArgs(string[] args, out bool firstRun, out string? outputDir, out string? onlyPage)
    {
        firstRun = false;
        outputDir = null;
        onlyPage = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--first-run":
                    firstRun = true;
                    break;
                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --output");
                        return false;
                    }

                    outputDir = args[++i];
                    break;
                case "--only":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --only");
                        return false;
                    }

                    onlyPage = args[++i];
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine(
                        """
                        Usage: THBWiki-Markdown-Builder [--first-run] [--output <dir>] [--only <title>]

                          --first-run     Extract archive.zip / *.tar and decompress .br HTML.
                          --output dir    Markdown output directory (default: %TEMP%\THBWikiMarkdown).
                          --only title    Rebuild a single page (and pages it newly links to).
                        """
                    );
                    return false;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return false;
            }
        }

        return true;
    }
}
