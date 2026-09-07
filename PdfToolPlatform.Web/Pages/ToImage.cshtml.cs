using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PdfToolPlatform.Core.Services;
using PdfToolPlatform.Web.Services;

namespace PdfToolPlatform.Web.Pages;

public class ToImageModel : PageModel
{
    private readonly PdfToImageService _toImageService;

    public ToImageModel(PdfToImageService toImageService)
    {
        _toImageService = toImageService;
    }

    [BindProperty]
    public IFormFile? File { get; set; }

    [BindProperty]
    public int Dpi { get; set; } = 150;

    [BindProperty]
    public ImageFormat Format { get; set; } = ImageFormat.Png;

    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (File is null)
        {
            ErrorMessage = "請先選擇PDF檔案。";
            return Page();
        }

        using var workspace = new TempWorkspace();

        try
        {
            var inputPath = workspace.GetFilePath("input.pdf");
            await using (var stream = System.IO.File.Create(inputPath))
            {
                await File.CopyToAsync(stream);
            }

            var outputDir = workspace.GetFilePath("output");
            Directory.CreateDirectory(outputDir);

            var outputs = _toImageService.ConvertToImages(inputPath, outputDir, Dpi, Format);

            if (outputs.Count == 1)
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(outputs[0]);
                var contentType = Format == ImageFormat.Png ? "image/png" : "image/jpeg";
                return File(bytes, contentType, Path.GetFileName(outputs[0]));
            }

            var zipPath = workspace.GetFilePath("images.zip");
            using (var zipStream = System.IO.File.Create(zipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var output in outputs)
                {
                    archive.CreateEntryFromFile(output, Path.GetFileName(output));
                }
            }

            var zipBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
            return File(zipBytes, "application/zip", "images.zip");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"處理失敗: {ex.Message}";
            return Page();
        }
    }
}
