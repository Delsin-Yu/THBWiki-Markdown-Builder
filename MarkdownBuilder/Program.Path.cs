internal partial class Program
{
    private static void CreatePaths(
        out string archiveZipPath,
        out string mainTarPath,
        out string fileTarPath,
        out string otherTarPath,
        out string tempDir,
        out string tempArchiveDir,
        out string tempMainDir,
        out string tempFileDir,
        out string tempOtherDir)
    {
        var root = Path.GetFullPath("./../../../../");
        var sourceDir = Path.Combine(root, "THBWikiSources");
        archiveZipPath = Path.Combine(sourceDir, "archive.zip");
        mainTarPath = Path.Combine(sourceDir, "main.tar");
        fileTarPath = Path.Combine(sourceDir, "file.tar");
        otherTarPath = Path.Combine(sourceDir, "other.tar");
        var markdownDir = Path.Combine(root, "THBWikiMarkdown");
        tempDir = Path.Combine(markdownDir, "Temp");
        tempArchiveDir = Path.Combine(tempDir, "archive");
        tempMainDir = Path.Combine(tempDir, "main");
        tempFileDir = Path.Combine(tempDir, "file");
        tempOtherDir = Path.Combine(tempDir, "other");
    }
}