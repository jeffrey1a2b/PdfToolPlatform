using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PdfToolPlatform.Core.Services;

namespace PdfToolPlatform.Web.Pages;

/// <summary>
/// 一筆暫存在合併工作區裡的檔案。Id用來對應畫面上的按鈕動作(刪除/上移/下移),
/// StoredFileName是實際落地在暫存資料夾裡的檔名(用GUID命名,避免同檔名衝突或路徑注入問題)。
/// </summary>
public class MergeFileEntry
{
    public string Id { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
}

/// <summary>
/// 合併頁面。相較單次上傳版本,這裡用Session維護一個「工作區」:
/// 使用者可以分好幾次加入檔案、隨時刪除某一筆、調整順序,最後才真正執行合併。
/// 工作區的檔案列表存在Session(JSON字串),實際檔案落地在該Session專屬的暫存資料夾。
/// </summary>
public class MergeModel : PageModel
{
    private const string SessionKey = "MergeFiles";
    private readonly PdfMergeService _mergeService;

    public MergeModel(PdfMergeService mergeService)
    {
        _mergeService = mergeService;
    }

    public List<MergeFileEntry> Entries { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        Entries = LoadEntries();
    }

    /// <summary>加入一或多個PDF檔案到工作區(可重複呼叫,累加式上傳)。</summary>
    public async Task<IActionResult> OnPostAddAsync(List<IFormFile> newFiles)
    {
        var entries = LoadEntries();

        if (newFiles is null || newFiles.Count == 0)
        {
            Entries = entries;
            ErrorMessage = "請選擇至少一個PDF檔案。";
            return Page();
        }

        var folder = GetSessionFolder();
        Directory.CreateDirectory(folder);

        int skippedNonPdf = 0;
        foreach (var file in newFiles)
        {
            if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                skippedNonPdf++;
                continue;
            }

            var id = Guid.NewGuid().ToString("N");
            var storedFileName = id + ".pdf";
            var path = Path.Combine(folder, storedFileName);

            await using (var stream = System.IO.File.Create(path))
            {
                await file.CopyToAsync(stream);
            }

            entries.Add(new MergeFileEntry
            {
                Id = id,
                OriginalName = file.FileName,
                StoredFileName = storedFileName
            });
        }

        SaveEntries(entries);

        if (skippedNonPdf > 0)
        {
            // 用TempData讓提示訊息能撐過redirect後仍顯示一次
            TempData["Notice"] = $"已略過 {skippedNonPdf} 個非PDF檔案。";
        }

        return RedirectToPage();
    }

    /// <summary>從工作區移除單一檔案。</summary>
    public IActionResult OnPostRemove([FromForm] string id)
    {
        var entries = LoadEntries();
        var target = entries.FirstOrDefault(e => e.Id == id);
        if (target is not null)
        {
            var path = Path.Combine(GetSessionFolder(), target.StoredFileName);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);

            entries.Remove(target);
            SaveEntries(entries);
        }

        return RedirectToPage();
    }

    /// <summary>將指定檔案往前移一個位置。</summary>
    public IActionResult OnPostMoveUp([FromForm] string id)
    {
        MoveEntry(id, -1);
        return RedirectToPage();
    }

    /// <summary>將指定檔案往後移一個位置。</summary>
    public IActionResult OnPostMoveDown([FromForm] string id)
    {
        MoveEntry(id, 1);
        return RedirectToPage();
    }

    /// <summary>清空整個工作區,不合併、重新開始。</summary>
    public IActionResult OnPostClear()
    {
        var folder = GetSessionFolder();
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        HttpContext.Session.Remove(SessionKey);
        return RedirectToPage();
    }

    /// <summary>依目前工作區的順序執行合併,回傳結果後清空工作區(檔案不留存)。</summary>
    public async Task<IActionResult> OnPostMergeAsync()
    {
        var entries = LoadEntries();

        if (entries.Count < 2)
        {
            Entries = entries;
            ErrorMessage = "請至少加入兩個PDF檔案才能合併。";
            return Page();
        }

        var folder = GetSessionFolder();
        var inputPaths = entries.Select(e => Path.Combine(folder, e.StoredFileName)).ToList();

        try
        {
            var outputPath = Path.Combine(folder, "merged_" + Guid.NewGuid().ToString("N") + ".pdf");
            _mergeService.Merge(inputPaths, outputPath);
            var bytes = await System.IO.File.ReadAllBytesAsync(outputPath);

            // 合併完成、檔案已讀進記憶體準備回應下載,工作區的原始檔與結果檔都可以清掉了
            CleanupWorkspace(folder, inputPaths, outputPath);
            HttpContext.Session.Remove(SessionKey);

            return File(bytes, "application/pdf", "merged.pdf");
        }
        catch (Exception ex)
        {
            Entries = entries;
            ErrorMessage = $"合併失敗: {ex.Message}";
            return Page();
        }
    }

    private static void CleanupWorkspace(string folder, IEnumerable<string> inputPaths, string outputPath)
    {
        try
        {
            foreach (var path in inputPaths)
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            if (System.IO.File.Exists(outputPath))
                System.IO.File.Delete(outputPath);

            if (Directory.Exists(folder) && Directory.GetFiles(folder).Length == 0)
                Directory.Delete(folder);
        }
        catch
        {
            // 個別檔案清除失敗不影響已完成的下載回應;背景清理服務會在之後補掃殘留檔案
        }
    }

    private void MoveEntry(string id, int direction)
    {
        var entries = LoadEntries();
        int index = entries.FindIndex(e => e.Id == id);
        if (index < 0) return;

        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= entries.Count) return;

        (entries[index], entries[newIndex]) = (entries[newIndex], entries[index]);
        SaveEntries(entries);
    }

    private List<MergeFileEntry> LoadEntries()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json)) return new List<MergeFileEntry>();
        return JsonSerializer.Deserialize<List<MergeFileEntry>>(json) ?? new List<MergeFileEntry>();
    }

    private void SaveEntries(List<MergeFileEntry> entries)
    {
        HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(entries));
    }

    /// <summary>
    /// 每個瀏覽器Session各自獨立的暫存資料夾,直接放在Web版共用的暫存根目錄下,
    /// 這樣既有的TempCleanupService背景清理服務也會一併掃到、清除逾時未合併的孤兒工作區。
    /// </summary>
    private string GetSessionFolder()
    {
        return Path.Combine(Path.GetTempPath(), "PdfToolPlatformWeb", "merge_" + HttpContext.Session.Id);
    }
}
