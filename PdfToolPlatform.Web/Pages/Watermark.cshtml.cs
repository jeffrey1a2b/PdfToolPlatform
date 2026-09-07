using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PdfToolPlatform.Core.Models;
using PdfToolPlatform.Core.Services;

namespace PdfToolPlatform.Web.Pages;

/// <summary>
/// 浮水印頁面。支援文字與圖片(Logo)兩種浮水印,並提供第一頁預覽功能。
/// 原始PDF與浮水印圖片都暫存在該瀏覽器Session專屬的資料夾,調整參數重複預覽不需要重新上傳。
/// </summary>
public class WatermarkModel : PageModel
{
    private readonly WatermarkService _watermarkService;
    private readonly PdfSplitService _splitService;
    private readonly PdfToImageService _toImageService;

    public WatermarkModel(WatermarkService watermarkService, PdfSplitService splitService, PdfToImageService toImageService)
    {
        _watermarkService = watermarkService;
        _splitService = splitService;
        _toImageService = toImageService;
    }

    [BindProperty]
    public IFormFile? File { get; set; }

    [BindProperty]
    public WatermarkType Type { get; set; } = WatermarkType.Text;

    [BindProperty]
    public string Text { get; set; } = "機密文件-僅供內部使用";

    [BindProperty]
    public IFormFile? WatermarkImageFile { get; set; }

    [BindProperty]
    public double ImageWidthPoints { get; set; } = 150;

    [BindProperty]
    public double Opacity { get; set; } = 0.25;

    [BindProperty]
    public double RotationDegrees { get; set; } = 0;

    [BindProperty]
    public WatermarkLayout Layout { get; set; } = WatermarkLayout.DiagonalSingle;

    [BindProperty]
    public double TileSpacingX { get; set; } = 350;

    [BindProperty]
    public double TileSpacingY { get; set; } = 175;

    [BindProperty]
    public double OffsetXPoints { get; set; } = 0;

    [BindProperty]
    public double OffsetYPoints { get; set; } = 300;

    public string? ErrorMessage { get; set; }

    /// <summary>預覽圖(第1頁)的Base64編碼,view直接嵌成 data URI 顯示,不落地成靜態檔案。</summary>
    public string? PreviewImageBase64 { get; set; }

    public bool HasStoredFile => System.IO.File.Exists(GetStoredSourcePath());

    public bool HasStoredImage => GetStoredImagePath() is not null;

    public void OnGet() { }

    /// <summary>只處理第一頁,轉成圖片回傳供頁面內嵌顯示,速度快、不用下載整份PDF。</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        EnsureSessionEstablished();

        var sourcePath = await ResolveSourceFileAsync();
        if (sourcePath is null)
        {
            ErrorMessage = "請先選擇PDF檔案。";
            return Page();
        }

        string? imagePath = null;
        if (Type == WatermarkType.Image)
        {
            imagePath = await ResolveWatermarkImageAsync();
            if (imagePath is null)
            {
                ErrorMessage = "請選擇浮水印圖片檔案。";
                return Page();
            }
        }

        var workDir = GetSessionFolder();
        var page1Path = Path.Combine(workDir, "preview_page1.pdf");
        var watermarkedPath = Path.Combine(workDir, "preview_watermarked.pdf");
        var imgDir = Path.Combine(workDir, "preview_img");

        try
        {
            _splitService.SplitByRange(sourcePath, page1Path, startPage: 1, endPage: 1);
            _watermarkService.ApplyWatermark(page1Path, watermarkedPath, BuildOptions(imagePath));

            Directory.CreateDirectory(imgDir);
            var images = _toImageService.ConvertToImages(watermarkedPath, imgDir, dpi: 120, format: ImageFormat.Png);

            if (images.Count > 0)
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(images[0]);
                PreviewImageBase64 = Convert.ToBase64String(bytes);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"預覽失敗: {ex.Message}";
        }
        finally
        {
            // 中介檔案(單頁抽取本、預覽用PDF、預覽圖)用完即刪,只保留原始檔/浮水印圖供之後重複使用
            SafeDelete(page1Path);
            SafeDelete(watermarkedPath);
            if (Directory.Exists(imgDir))
            {
                try { Directory.Delete(imgDir, recursive: true); } catch { /* 忽略清除失敗 */ }
            }
        }

        return Page();
    }

    /// <summary>對整份PDF套用浮水印並回傳下載。</summary>
    public async Task<IActionResult> OnPostApplyAsync()
    {
        EnsureSessionEstablished();

        var sourcePath = await ResolveSourceFileAsync();
        if (sourcePath is null)
        {
            ErrorMessage = "請先選擇PDF檔案。";
            return Page();
        }

        string? imagePath = null;
        if (Type == WatermarkType.Image)
        {
            imagePath = await ResolveWatermarkImageAsync();
            if (imagePath is null)
            {
                ErrorMessage = "請選擇浮水印圖片檔案。";
                return Page();
            }
        }

        var workDir = GetSessionFolder();
        var outputPath = Path.Combine(workDir, "watermarked_result.pdf");

        try
        {
            _watermarkService.ApplyWatermark(sourcePath, outputPath, BuildOptions(imagePath));
            var bytes = await System.IO.File.ReadAllBytesAsync(outputPath);
            return File(bytes, "application/pdf", "watermarked.pdf");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"處理失敗: {ex.Message}";
            return Page();
        }
        finally
        {
            SafeDelete(outputPath);
        }
    }

    private WatermarkOptions BuildOptions(string? imagePath) => new()
    {
        Type = Type,
        Text = Text,
        ImagePath = imagePath,
        ImageWidthPoints = ImageWidthPoints,
        Opacity = Opacity,
        RotationDegrees = RotationDegrees,
        Layout = Layout,
        TileSpacingX = TileSpacingX,
        TileSpacingY = TileSpacingY,
        OffsetXPoints = OffsetXPoints,
        OffsetYPoints = OffsetYPoints
    };

    /// <summary>
    /// 若這次請求有新上傳PDF,存成該Session的原始檔(覆蓋舊的);
    /// 若沒有新上傳(例如只是調整參數重新預覽),沿用先前已暫存的原始檔。
    /// </summary>
    private async Task<string?> ResolveSourceFileAsync()
    {
        var storedPath = GetStoredSourcePath();

        if (File is not null && File.Length > 0)
        {
            Directory.CreateDirectory(GetSessionFolder());
            await using var stream = System.IO.File.Create(storedPath);
            await File.CopyToAsync(stream);
            return storedPath;
        }

        return System.IO.File.Exists(storedPath) ? storedPath : null;
    }

    /// <summary>
    /// 同樣的「有新上傳就覆蓋、沒有就沿用暫存」邏輯,套用在浮水印圖片上。
    /// 用萬用字元找檔案是因為要保留原始副檔名(png/jpg等),PDFsharp/ImageSharp依副檔名判斷格式。
    /// </summary>
    private async Task<string?> ResolveWatermarkImageAsync()
    {
        if (WatermarkImageFile is not null && WatermarkImageFile.Length > 0)
        {
            var dir = GetSessionFolder();
            Directory.CreateDirectory(dir);

            // 副檔名可能跟前一次不同,先清掉舊的浮水印圖片,避免同資料夾出現多張造成混淆
            foreach (var old in Directory.GetFiles(dir, "watermark_image.*"))
                SafeDelete(old);

            var ext = Path.GetExtension(WatermarkImageFile.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var newPath = Path.Combine(dir, "watermark_image" + ext);

            await using var stream = System.IO.File.Create(newPath);
            await WatermarkImageFile.CopyToAsync(stream);
            return newPath;
        }

        return GetStoredImagePath();
    }

    private string? GetStoredImagePath()
    {
        var dir = GetSessionFolder();
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "watermark_image.*").FirstOrDefault();
    }

    /// <summary>
    /// 只是讀取Session.Id並不保證Cookie會確實送出,寫入一個小值強制建立/延續Session,
    /// 確保這次存的暫存檔在下一次請求(例如調整參數後再按預覽)還能被同一支Session找到。
    /// </summary>
    private void EnsureSessionEstablished()
    {
        HttpContext.Session.SetString("wm_touch", DateTime.UtcNow.Ticks.ToString());
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
        catch
        {
            // 忽略單一暫存檔清除失敗,不影響主要流程;背景清理服務會之後補掃
        }
    }

    private string GetSessionFolder() => Path.Combine(Path.GetTempPath(), "PdfToolPlatformWeb", "watermark_" + HttpContext.Session.Id);

    private string GetStoredSourcePath() => Path.Combine(GetSessionFolder(), "source.pdf");
}
