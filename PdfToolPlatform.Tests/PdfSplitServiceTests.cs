using PdfSharp.Pdf;
using PdfToolPlatform.Core.Services;
using Xunit;

namespace PdfToolPlatform.Tests;

public class PdfSplitServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfSplitService _service = new();

    public PdfSplitServiceTests()
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
    public void SplitByFixedSize_ExactMultiple_ProducesExpectedFileCount()
    {
        var file = CreateSamplePdf("source.pdf", 10);
        var outDir = Path.Combine(_tempDir, "out1");

        var results = _service.SplitByFixedSize(file, outDir, 5);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SplitByFixedSize_NonExactMultiple_LastFileHasRemainder()
    {
        // 10頁、每3頁一份 -> 3+3+3+1,共4份,最後一份應只有1頁
        var file = CreateSamplePdf("source.pdf", 10);
        var outDir = Path.Combine(_tempDir, "out2");

        var results = _service.SplitByFixedSize(file, outDir, 3);

        Assert.Equal(4, results.Count);
        using var lastFile = PdfSharp.Pdf.IO.PdfReader.Open(results[^1], PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(1, lastFile.PageCount);
    }

    [Fact]
    public void SplitByFixedSize_ZeroOrNegativePageCount_ThrowsArgumentOutOfRange()
    {
        var file = CreateSamplePdf("source.pdf", 5);
        var outDir = Path.Combine(_tempDir, "out3");

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SplitByFixedSize(file, outDir, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SplitByFixedSize(file, outDir, -1));
    }

    [Fact]
    public void SplitByRange_ValidRange_ProducesCorrectPageCount()
    {
        var file = CreateSamplePdf("source.pdf", 10);
        var output = Path.Combine(_tempDir, "range_out.pdf");

        _service.SplitByRange(file, output, startPage: 3, endPage: 7);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(5, result.PageCount); // 頁3,4,5,6,7
    }

    [Fact]
    public void SplitByRange_StartGreaterThanEnd_ThrowsArgumentOutOfRange()
    {
        var file = CreateSamplePdf("source.pdf", 10);
        var output = Path.Combine(_tempDir, "invalid_out.pdf");

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SplitByRange(file, output, startPage: 8, endPage: 3));
    }

    [Fact]
    public void SplitByRange_EndPageExceedsTotalPages_ThrowsArgumentOutOfRange()
    {
        var file = CreateSamplePdf("source.pdf", 5);
        var output = Path.Combine(_tempDir, "invalid_out2.pdf");

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SplitByRange(file, output, startPage: 1, endPage: 99));
    }

    [Fact]
    public void SplitByRange_SinglePageRange_ProducesOnePage()
    {
        var file = CreateSamplePdf("source.pdf", 5);
        var output = Path.Combine(_tempDir, "single_page.pdf");

        _service.SplitByRange(file, output, startPage: 1, endPage: 1);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void SplitByMultipleRanges_ThreeRanges_ProducesThreeFiles()
    {
        var file = CreateSamplePdf("source.pdf", 12);
        var outDir = Path.Combine(_tempDir, "multi_out");

        var results = _service.SplitByMultipleRanges(file, outDir, new[] { (1, 3), (4, 10), (11, 12) });

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void SplitByFixedSize_NonExistentFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "missing.pdf");
        var outDir = Path.Combine(_tempDir, "out_missing");

        Assert.Throws<FileNotFoundException>(() => _service.SplitByFixedSize(missing, outDir, 2));
    }
}
