using PdfSharp.Fonts;

namespace PdfToolPlatform.Core.Fonts;

/// <summary>
/// PDFsharp 6.x 的核心套件(非GDI版)不會自動使用作業系統安裝的字型,
/// 必須自己實作 IFontResolver 告訴它字型檔案在哪裡,否則會拋出
/// "No appropriate font found for family name..." 的例外。
///
/// 這裡直接從 Windows 的系統字型資料夾(通常是 C:\Windows\Fonts)讀取常見的
/// 繁中/簡中/英文字型檔案。因為本專案就是Windows限定的內部工具,這個做法比
/// 額外打包字型檔案更單純,也不需要處理字型授權問題(直接沿用主機上系統
/// 內建的Windows字型)。
///
/// 使用方式:在應用程式啟動時執行一次
///     GlobalFontSettings.FontResolver = new WindowsFontResolver();
/// (Web版在Program.cs、桌面版在App.xaml.cs)
/// </summary>
public class WindowsFontResolver : IFontResolver
{
    private static readonly string FontsDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    /// <summary>
    /// 家族名稱(不分大小寫)對應到Windows字型檔案名稱(Regular / Bold)。
    /// 涵蓋常見的繁中、簡中、英文字型;若UI上要開放更多字型選項,在這裡擴充即可。
    /// </summary>
    private static readonly Dictionary<string, (string Regular, string? Bold)> FamilyMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft JhengHei"] = ("msjh.ttc", "msjhbd.ttc"),
            ["微軟正黑體"] = ("msjh.ttc", "msjhbd.ttc"),
            ["Microsoft YaHei"] = ("msyh.ttc", "msyhbd.ttc"),
            ["PMingLiU"] = ("mingliu.ttc", null),
            ["新細明體"] = ("mingliu.ttc", null),
            ["SimSun"] = ("simsun.ttc", null),
            ["Arial"] = ("arial.ttf", "arialbd.ttf"),
            ["Times New Roman"] = ("times.ttf", "timesbd.ttf"),
        };

    /// <summary>當請求的家族名稱不在對照表裡時的後備字型(繁中環境下最常見的字型)。</summary>
    private const string FallbackFamily = "Microsoft JhengHei";

    public byte[] GetFont(string faceName)
    {
        var path = Path.Combine(FontsDirectory, faceName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"找不到字型檔案 \"{faceName}\"(預期路徑: {path})。" +
                "請確認主機的Windows有安裝對應字型,或改用其他已安裝的字型名稱。",
                path);
        }

        var fileBytes = File.ReadAllBytes(path);

        // 對照表裡繁中/簡中字型(msjh.ttc、mingliu.ttc、simsun.ttc等)都是TrueType Collection
        // (.ttc)容器格式,一個檔案裡包了多個字型並共用部分表格資料。PDFsharp的字型解析器
        // 不認得.ttc容器,會把它誤判成一般SFNT字型去解析,導致量測/繪製文字時發生
        // NullReferenceException。這裡一律先過一層.ttc偵測/抽取,把容器裡第一個子字型
        // 重組成獨立、合法的SFNT資料;若本來就是一般.ttf/.otf則原樣直接回傳。
        return TrueTypeCollectionExtractor.ExtractFontBytes(fileBytes);
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var key = FamilyMap.ContainsKey(familyName) ? familyName : FallbackFamily;
        var (regular, bold) = FamilyMap[key];

        // 目前浮水印功能不使用斜體,粗體則盡量對應到對照表裡的粗體字型檔;
        // 若該家族沒有獨立的粗體檔案,退回用一般字重(避免整個解析失敗)。
        var fileName = isBold && bold is not null ? bold : regular;

        return new FontResolverInfo(fileName);
    }
}
