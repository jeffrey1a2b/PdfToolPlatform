using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfToolPlatform.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfToolPlatform.Core.Services;

/// <summary>
/// 浮水印服務。支援文字與圖片(Logo)兩種內容類型,共用同一套排版邏輯
/// (置中單一 / 斜角單一 / 滿版平舖)。
///
/// 透明度處理方式:
/// - 文字:直接用 XSolidBrush 的 alpha 色版
/// - 圖片:PDFsharp對圖片的透明度支援因版本而異,為了穩定一致的效果,
///   改成先把透明度"烘進"圖片本身的alpha色版(用ImageSharp處理成暫存PNG),
///   再把這張已經半透明的PNG嵌入PDF,無論PDFsharp版本都能正確呈現。
/// </summary>
public class WatermarkService
{
    public void ApplyWatermark(string inputPath, string outputPath, WatermarkOptions options)
    {
        options.Validate();

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"找不到檔案: {inputPath}", inputPath);

        using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

        if (options.Type == WatermarkType.Text)
        {
            ApplyTextWatermark(document, options);
        }
        else
        {
            ApplyImageWatermark(document, options);
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        document.Save(outputPath);
    }

    private static void ApplyTextWatermark(PdfDocument document, WatermarkOptions options)
    {
        int alpha = (int)Math.Round(options.Opacity * 255);
        var color = XColor.FromArgb(alpha, options.ColorR, options.ColorG, options.ColorB);
        var font = new XFont(options.FontName, options.FontSize);
        var brush = new XSolidBrush(color);

        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page);
            var size = gfx.MeasureString(options.Text, font);

            void DrawContent(XGraphics g) => g.DrawString(
                options.Text, font, brush,
                new XRect(-size.Width / 2, -size.Height / 2, size.Width, size.Height),
                XStringFormats.Center);

            DrawByLayout(gfx, page.Width.Point, page.Height.Point, options, size.Width, size.Height, DrawContent);
        }
    }

    private static void ApplyImageWatermark(PdfDocument document, WatermarkOptions options)
    {
        if (string.IsNullOrEmpty(options.ImagePath) || !File.Exists(options.ImagePath))
            throw new FileNotFoundException("找不到浮水印圖片檔案", options.ImagePath ?? string.Empty);

        var processedImagePath = BuildOpacityBakedImage(options.ImagePath, options.Opacity);

        try
        {
            using var ximage = XImage.FromFile(processedImagePath);

            double widthPt = options.ImageWidthPoints;
            double heightPt = widthPt * (ximage.PixelHeight / (double)ximage.PixelWidth);

            foreach (var page in document.Pages)
            {
                using var gfx = XGraphics.FromPdfPage(page);

                void DrawContent(XGraphics g) => g.DrawImage(ximage, -widthPt / 2, -heightPt / 2, widthPt, heightPt);

                DrawByLayout(gfx, page.Width.Point, page.Height.Point, options, widthPt, heightPt, DrawContent);
            }
        }
        finally
        {
            SafeDelete(processedImagePath);
        }
    }

    /// <summary>
    /// 把指定透明度烘進圖片的alpha色版,輸出成暫存PNG。
    /// 即使來源是不含透明度的JPG,輸出後也會是均勻半透明的PNG。
    /// </summary>
    private static string BuildOpacityBakedImage(string sourcePath, double opacity)
    {
        using var image = Image.Load<Rgba32>(sourcePath);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];
                    pixel.A = (byte)Math.Round(pixel.A * opacity);
                }
            }
        });

        var tempPath = Path.Combine(Path.GetTempPath(), $"pdftoolplatform_wm_{Guid.NewGuid():N}.png");
        image.SaveAsPng(tempPath);
        return tempPath;
    }

    private static void DrawByLayout(
        XGraphics gfx, double pageWidth, double pageHeight, WatermarkOptions options,
        double contentWidth, double contentHeight, Action<XGraphics> drawContentCentered)
    {
        switch (options.Layout)
        {
            case WatermarkLayout.Center:
            case WatermarkLayout.DiagonalSingle:
                DrawSingle(
                    gfx,
                    pageWidth / 2 + options.OffsetXPoints,
                    pageHeight / 2 + options.OffsetYPoints,
                    options.RotationDegrees,
                    drawContentCentered);
                break;

            case WatermarkLayout.TiledRepeat:
                DrawTiled(gfx, pageWidth, pageHeight, options, contentWidth, contentHeight, drawContentCentered);
                break;
        }
    }

    private static void DrawSingle(XGraphics gfx, double centerX, double centerY, double rotationDegrees, Action<XGraphics> drawContentCentered)
    {
        gfx.Save();
        gfx.TranslateTransform(centerX, centerY);
        gfx.RotateTransform(-rotationDegrees); // PDFsharp為順時針正角度,取負值符合一般"左下到右上"直覺
        drawContentCentered(gfx);
        gfx.Restore();
    }

    private static void DrawTiled(
        XGraphics gfx, double pageWidth, double pageHeight, WatermarkOptions options,
        double contentWidth, double contentHeight, Action<XGraphics> drawContentCentered)
    {
        // 旋轉後的實際包圍盒會變大,抓保守值(對角線長度)避免格線間穿插斷字/斷圖
        double diag = Math.Sqrt(contentWidth * contentWidth + contentHeight * contentHeight);

        double startX = -diag + options.OffsetXPoints;
        double startY = -diag + options.OffsetYPoints;
        double endX = pageWidth + diag + options.OffsetXPoints;
        double endY = pageHeight + diag + options.OffsetYPoints;

        for (double y = startY; y <= endY; y += options.TileSpacingY)
        {
            for (double x = startX; x <= endX; x += options.TileSpacingX)
            {
                DrawSingle(gfx, x, y, options.RotationDegrees, drawContentCentered);
            }
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 暫存圖檔清除失敗不影響主要流程
        }
    }
}
