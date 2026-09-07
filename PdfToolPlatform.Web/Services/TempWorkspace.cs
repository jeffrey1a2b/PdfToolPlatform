namespace PdfToolPlatform.Web.Services;

/// <summary>
/// 每次操作建立一個獨立、隨機命名的暫存資料夾,用完即刪。
/// 這是Web版相較桌面版多出來的安全考量:桌面版檔案留在使用者自己電腦,
/// Web版檔案會短暫落地在主機上,必須確保處理完立刻清除,不留存。
/// </summary>
public class TempWorkspace : IDisposable
{
    public string Path { get; }

    public TempWorkspace()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PdfToolPlatformWeb", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string GetFilePath(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // 刪除失敗不應影響回應已完成的請求;背景清理任務會定期補掃殘留的暫存資料夾
        }
    }
}
