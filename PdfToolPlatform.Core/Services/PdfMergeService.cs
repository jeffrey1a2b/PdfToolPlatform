using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfToolPlatform.Core.Services;

/// <summary>
/// PDF 合併服務。所有處理均在本機記憶體中完成,不會呼叫任何網路端點。
/// </summary>
public class PdfMergeService
{
    /// <summary>
    /// 依指定順序合併多個PDF檔案為單一檔案。
    /// </summary>
    /// <param name="inputPaths">來源PDF路徑,依合併順序排列</param>
    /// <param name="outputPath">輸出路徑</param>
    /// <exception cref="ArgumentException">來源清單為空</exception>
    /// <exception cref="FileNotFoundException">任一來源檔案不存在</exception>
    public void Merge(IReadOnlyList<string> inputPaths, string outputPath)
    {
        if (inputPaths is null || inputPaths.Count == 0)
            throw new ArgumentException("至少需要一個來源檔案", nameof(inputPaths));

        foreach (var path in inputPaths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"找不到檔案: {path}", path);
        }

        using var outputDocument = new PdfDocument();

        foreach (var path in inputPaths)
        {
            // Import 模式開啟,確保不會意外修改/鎖住原始檔案
            using var inputDocument = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                outputDocument.AddPage(inputDocument.Pages[i]);
            }
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        outputDocument.Save(outputPath);
    }
}
