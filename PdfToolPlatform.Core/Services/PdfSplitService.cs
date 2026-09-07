using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfToolPlatform.Core.Services;

/// <summary>
/// PDF 分割服務。
/// </summary>
public class PdfSplitService
{
    /// <summary>
    /// 依固定頁數區間拆分。例如10頁的檔案、每3頁一份,會產生4份(3+3+3+1)。
    /// </summary>
    /// <param name="inputPath">來源PDF路徑</param>
    /// <param name="outputDirectory">輸出資料夾</param>
    /// <param name="pagesPerFile">每份檔案的頁數,須大於0</param>
    /// <returns>產出的檔案路徑清單</returns>
    public List<string> SplitByFixedSize(string inputPath, string outputDirectory, int pagesPerFile)
    {
        if (pagesPerFile <= 0)
            throw new ArgumentOutOfRangeException(nameof(pagesPerFile), "每份頁數必須大於0");
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"找不到檔案: {inputPath}", inputPath);

        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outputPaths = new List<string>();

        using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        int totalPages = source.PageCount;
        int fileIndex = 1;

        for (int start = 0; start < totalPages; start += pagesPerFile)
        {
            int end = Math.Min(start + pagesPerFile, totalPages);
            using var chunk = new PdfDocument();
            for (int i = start; i < end; i++)
                chunk.AddPage(source.Pages[i]);

            var outputPath = Path.Combine(outputDirectory, $"{baseName}_part{fileIndex}.pdf");
            chunk.Save(outputPath);
            outputPaths.Add(outputPath);
            fileIndex++;
        }

        return outputPaths;
    }

    /// <summary>
    /// 依指定頁碼範圍抽出單一檔案(1-based頁碼,含頭尾)。
    /// </summary>
    /// <param name="inputPath">來源PDF路徑</param>
    /// <param name="outputPath">輸出路徑</param>
    /// <param name="startPage">起始頁碼(從1開始)</param>
    /// <param name="endPage">結束頁碼(含)</param>
    public void SplitByRange(string inputPath, string outputPath, int startPage, int endPage)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"找不到檔案: {inputPath}", inputPath);

        using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        int totalPages = source.PageCount;

        if (startPage < 1 || endPage > totalPages || startPage > endPage)
            throw new ArgumentOutOfRangeException(
                nameof(startPage), $"頁碼範圍無效,檔案共有{totalPages}頁,收到範圍 {startPage}-{endPage}");

        using var result = new PdfDocument();
        for (int i = startPage - 1; i < endPage; i++)
            result.AddPage(source.Pages[i]);

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        result.Save(outputPath);
    }

    /// <summary>
    /// 依多組頁碼範圍拆成多個檔案,例如 "1-3,4-10,11-12"。
    /// </summary>
    public List<string> SplitByMultipleRanges(string inputPath, string outputDirectory, IEnumerable<(int start, int end)> ranges)
    {
        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outputPaths = new List<string>();
        int index = 1;

        foreach (var (start, end) in ranges)
        {
            var outputPath = Path.Combine(outputDirectory, $"{baseName}_range{index}_{start}-{end}.pdf");
            SplitByRange(inputPath, outputPath, start, end);
            outputPaths.Add(outputPath);
            index++;
        }

        return outputPaths;
    }
}
