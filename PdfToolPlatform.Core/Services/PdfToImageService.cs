using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfToolPlatform.Core.Services;

public enum ImageFormat
{
    Png,
    Jpeg
}

/// <summary>
/// PDF 轉圖檔服務。以 Docnet.Core(封裝 PDFium 原生渲染引擎)在本機渲染,
/// 全程不經過網路,渲染出的 bitmap 直接落地存檔。
/// </summary>
public class PdfToImageService
{
    /// <summary>
    /// 將PDF每一頁轉成圖檔。
    /// </summary>
    /// <param name="inputPath">來源PDF路徑</param>
    /// <param name="outputDirectory">輸出資料夾</param>
    /// <param name="dpi">解析度,預設150,列印用建議300</param>
    /// <param name="format">輸出格式</param>
    /// <param name="jpegQuality">JPEG品質(1-100),format=Jpeg時生效</param>
    /// <returns>產出的圖檔路徑清單,依頁碼排序</returns>
    public List<string> ConvertToImages(
        string inputPath,
        string outputDirectory,
        int dpi = 150,
        ImageFormat format = ImageFormat.Png,
        int jpegQuality = 90)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"找不到檔案: {inputPath}", inputPath);
        if (dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI必須大於0");

        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outputPaths = new List<string>();

        // A4在96dpi下約為 794x1123 px,這裡依比例換算成指定dpi下的目標寬高
        // Docnet 用 PageDimensions 指定渲染的目標像素尺寸
        const int baseDpi = 96;
        var scale = dpi / (double)baseDpi;

        using var docReader = DocLib.Instance.GetDocReader(
            inputPath,
            new PageDimensions(scale));

        int pageCount = docReader.GetPageCount();

        for (int i = 0; i < pageCount; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();
            var rawBytes = pageReader.GetImage(); // BGRA byte array

            using var image = new Image<Bgra32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    int rowOffset = y * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = rowOffset + x * 4;
                        row[x] = new Bgra32(rawBytes[idx + 2], rawBytes[idx + 1], rawBytes[idx], rawBytes[idx + 3]);
                    }
                }
            });

            string ext = format == ImageFormat.Png ? "png" : "jpg";
            string outputPath = Path.Combine(outputDirectory, $"{baseName}_page{i + 1:D3}.{ext}");

            if (format == ImageFormat.Png)
            {
                image.SaveAsPng(outputPath);
            }
            else
            {
                var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = jpegQuality };
                image.SaveAsJpeg(outputPath, encoder);
            }

            outputPaths.Add(outputPath);
        }

        return outputPaths;
    }
}
