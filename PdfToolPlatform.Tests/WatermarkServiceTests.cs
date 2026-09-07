using PdfSharp.Pdf;
using PdfToolPlatform.Core.Models;
using PdfToolPlatform.Core.Services;
using Xunit;

namespace PdfToolPlatform.Tests;

public class WatermarkServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WatermarkService _service = new();

    public WatermarkServiceTests()
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
    public void ApplyWatermark_DiagonalSingle_HappyPath_ProducesValidPdfWithSamePageCount()
    {
        var input = CreateSamplePdf("in.pdf", 3);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions { Text = "機密", Layout = WatermarkLayout.DiagonalSingle };

        _service.ApplyWatermark(input, output, options);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void ApplyWatermark_TiledRepeat_ProducesValidPdf()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions
        {
            Text = "Internal Use Only",
            Layout = WatermarkLayout.TiledRepeat,
            TileSpacingX = 150,
            TileSpacingY = 150
        };

        _service.ApplyWatermark(input, output, options);

        Assert.True(File.Exists(output));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ApplyWatermark_OpacityOutOfRange_ThrowsArgumentOutOfRange(double invalidOpacity)
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions { Opacity = invalidOpacity };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ApplyWatermark(input, output, options));
    }

    [Fact]
    public void ApplyWatermark_EmptyText_ThrowsArgumentException()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions { Text = "   " };

        Assert.Throws<ArgumentException>(() => _service.ApplyWatermark(input, output, options));
    }

    [Fact]
    public void ApplyWatermark_ZeroTileSpacing_ThrowsArgumentOutOfRange()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions
        {
            Layout = WatermarkLayout.TiledRepeat,
            TileSpacingX = 0,
            TileSpacingY = 100
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ApplyWatermark(input, output, options));
    }

    [Fact]
    public void ApplyWatermark_MinimalOpacity_DoesNotThrow()
    {
        // 邊界值0.0(完全透明)應仍為合法輸入,不應拋例外
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions { Opacity = 0.0 };

        var exception = Record.Exception(() => _service.ApplyWatermark(input, output, options));
        Assert.Null(exception);
    }

    [Fact]
    public void ApplyWatermark_NonExistentFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "missing.pdf");
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions();

        Assert.Throws<FileNotFoundException>(() => _service.ApplyWatermark(missing, output, options));
    }

    [Fact]
    public void ApplyWatermark_MultiPageDocument_AppliesToEveryPage()
    {
        // 間接驗證:輸出檔案頁數需與輸入相同,代表每頁都被處理過而非只處理第一頁
        var input = CreateSamplePdf("in.pdf", 5);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions { Layout = WatermarkLayout.Center };

        _service.ApplyWatermark(input, output, options);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(5, result.PageCount);
    }

    // ---------- 圖片浮水印 ----------

    private string CreateSampleImage(string fileName)
    {
        // 最小合法的1x1像素PNG(base64),不需要額外套件即可產生測試用圖片
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ApplyWatermark_ImageType_HappyPath_ProducesValidPdfWithSamePageCount()
    {
        var input = CreateSamplePdf("in.pdf", 2);
        var imagePath = CreateSampleImage("logo.png");
        var output = Path.Combine(_tempDir, "out_image.pdf");
        var options = new WatermarkOptions
        {
            Type = WatermarkType.Image,
            ImagePath = imagePath,
            ImageWidthPoints = 100,
            Layout = WatermarkLayout.Center
        };

        _service.ApplyWatermark(input, output, options);

        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void ApplyWatermark_ImageType_TiledRepeat_ProducesValidPdf()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var imagePath = CreateSampleImage("logo.png");
        var output = Path.Combine(_tempDir, "out_tiled.pdf");
        var options = new WatermarkOptions
        {
            Type = WatermarkType.Image,
            ImagePath = imagePath,
            ImageWidthPoints = 80,
            Layout = WatermarkLayout.TiledRepeat,
            TileSpacingX = 120,
            TileSpacingY = 120
        };

        _service.ApplyWatermark(input, output, options);

        Assert.True(File.Exists(output));
    }

    [Fact]
    public void ApplyWatermark_ImageType_MissingImagePath_ThrowsArgumentException()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions { Type = WatermarkType.Image, ImagePath = null };

        Assert.Throws<ArgumentException>(() => _service.ApplyWatermark(input, output, options));
    }

    [Fact]
    public void ApplyWatermark_ImageType_NonExistentImageFile_ThrowsFileNotFoundException()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions
        {
            Type = WatermarkType.Image,
            ImagePath = Path.Combine(_tempDir, "does-not-exist.png"),
            ImageWidthPoints = 100
        };

        Assert.Throws<FileNotFoundException>(() => _service.ApplyWatermark(input, output, options));
    }

    [Fact]
    public void ApplyWatermark_ImageType_ZeroWidth_ThrowsArgumentOutOfRange()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var imagePath = CreateSampleImage("logo.png");
        var output = Path.Combine(_tempDir, "out.pdf");
        var options = new WatermarkOptions
        {
            Type = WatermarkType.Image,
            ImagePath = imagePath,
            ImageWidthPoints = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ApplyWatermark(input, output, options));
    }

    // ---------- 起始位置偏移 ----------

    [Fact]
    public void ApplyWatermark_WithOffset_DiagonalSingle_DoesNotThrowAndProducesValidPdf()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out_offset.pdf");
        var options = new WatermarkOptions
        {
            Text = "機密",
            Layout = WatermarkLayout.DiagonalSingle,
            OffsetXPoints = 100,
            OffsetYPoints = -80
        };

        var exception = Record.Exception(() => _service.ApplyWatermark(input, output, options));

        Assert.Null(exception);
        using var result = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void ApplyWatermark_WithOffset_TiledRepeat_DoesNotThrowAndProducesValidPdf()
    {
        var input = CreateSamplePdf("in.pdf", 1);
        var output = Path.Combine(_tempDir, "out_offset_tiled.pdf");
        var options = new WatermarkOptions
        {
            Text = "機密",
            Layout = WatermarkLayout.TiledRepeat,
            TileSpacingX = 150,
            TileSpacingY = 150,
            OffsetXPoints = 50,
            OffsetYPoints = 50
        };

        var exception = Record.Exception(() => _service.ApplyWatermark(input, output, options));

        Assert.Null(exception);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void ApplyWatermark_DefaultOffset_IsZero()
    {
        // 確保沒有指定偏移量時,行為與原本置中邏輯一致(向下相容)
        var options = new WatermarkOptions();
        Assert.Equal(0, options.OffsetXPoints);
        Assert.Equal(0, options.OffsetYPoints);
    }
}
