namespace PdfToolPlatform.Web.Services;

/// <summary>
/// 保險機制:定期掃描並刪除超過閒置時間的暫存資料夾,
/// 避免異常中斷(例如使用者關瀏覽器、程序崩潰)導致敏感檔案長期留在主機上。
/// </summary>
public class TempCleanupService : BackgroundService
{
    private static readonly string RootPath = Path.Combine(Path.GetTempPath(), "PdfToolPlatformWeb");
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    foreach (var dir in Directory.GetDirectories(RootPath))
                    {
                        var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                        if (DateTime.UtcNow - lastWrite > MaxAge)
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                    }
                }
            }
            catch
            {
                // 清理任務不應讓整個應用程式崩潰,下一輪再試即可
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }
}
