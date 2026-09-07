using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PdfToolPlatform.Core.Services;
using PdfToolPlatform.Web.Services;

namespace PdfToolPlatform.Web.Pages;

public class SplitModel : PageModel
{
    private readonly PdfSplitService _splitService;

    public SplitModel(PdfSplitService splitService)
    {
        _splitService = splitService;
    }

    [BindProperty]
    public IFormFile? File { get; set; }

    [BindProperty]
    public string Mode { get; set; } = "fixed";

    [BindProperty]
    public int PagesPerFile { get; set; } = 1;

    [BindProperty]
    public string Ranges { get; set; } = string.Empty;

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

            List<string> outputs;
            if (Mode == "range")
            {
                var ranges = ParseRanges(Ranges);
                outputs = _splitService.SplitByMultipleRanges(inputPath, outputDir, ranges);
            }
            else
            {
                if (PagesPerFile <= 0)
                {
                    ErrorMessage = "每份頁數必須大於0。";
                    return Page();
                }
                outputs = _splitService.SplitByFixedSize(inputPath, outputDir, PagesPerFile);
            }

            if (outputs.Count == 1)
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(outputs[0]);
                return File(bytes, "application/pdf", Path.GetFileName(outputs[0]));
            }

            var zipPath = workspace.GetFilePath("split_result.zip");
            using (var zipStream = System.IO.File.Create(zipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var output in outputs)
                {
                    archive.CreateEntryFromFile(output, Path.GetFileName(output));
                }
            }

            var zipBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
            return File(zipBytes, "application/zip", "split_result.zip");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"處理失敗: {ex.Message}";
            return Page();
        }
    }

    private static IEnumerable<(int start, int end)> ParseRanges(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("請輸入至少一組頁碼範圍");

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('-', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || !int.TryParse(pieces[0], out var start) || !int.TryParse(pieces[1], out var end))
                throw new FormatException($"範圍格式錯誤: \"{part}\",請使用如 1-3 的格式,多組範圍以逗號分隔");
            yield return (start, end);
        }
    }
}
