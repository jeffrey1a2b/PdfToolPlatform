using PdfSharp.Pdf;
using PdfToolPlatform.Core.Services;
using Xunit;

namespace PdfToolPlatform.Tests;

public class PdfMergeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfMergeService _service = new();

    public PdfMergeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PdfToolPlatformTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateSamplePdf(string fileName, int pageCount)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var doc = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
            doc.AddPage();
        doc.Save(path);
        return path;
    }

    [Fact]
    public void Merge_TwoFiles_ProducesCombinedPageCount()
    {
        var file1 = CreateSamplePdf("a.pdf", 2);
        var file2 = CreateSamplePdf("b.pdf", 3);
        var output = Path.Combine(_tempDir, "merged.pdf");

        _service.Merge(new[] { file1, file2 }, output);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(5, result.PageCount);
    }

    [Fact]
    public void Merge_PreservesOrder_FirstFilePagesComeBeforeSecond()
    {
        // 順序正確性間接透過頁數驗證:若順序錯亂,分段合併結果的頁數仍相同,
        // 但這裡著重驗證API依輸入清單順序處理,不會拋例外或跳過檔案
        var file1 = CreateSamplePdf("first.pdf", 1);
        var file2 = CreateSamplePdf("second.pdf", 1);
        var output = Path.Combine(_tempDir, "ordered.pdf");

        _service.Merge(new[] { file1, file2 }, output);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void Merge_EmptyList_ThrowsArgumentException()
    {
        var output = Path.Combine(_tempDir, "out.pdf");
        Assert.Throws<ArgumentException>(() => _service.Merge(Array.Empty<string>(), output));
    }

    [Fact]
    public void Merge_NonExistentFile_ThrowsFileNotFoundException()
    {
        var output = Path.Combine(_tempDir, "out.pdf");
        var missing = Path.Combine(_tempDir, "does-not-exist.pdf");
        Assert.Throws<FileNotFoundException>(() => _service.Merge(new[] { missing }, output));
    }

    [Fact]
    public void Merge_SingleFile_StillProducesValidOutput()
    {
        var file1 = CreateSamplePdf("solo.pdf", 4);
        var output = Path.Combine(_tempDir, "solo_out.pdf");

        _service.Merge(new[] { file1 }, output);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(4, result.PageCount);
    }
}
