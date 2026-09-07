namespace PdfToolPlatform.Core.Models;

/// <summary>
/// 浮水印排版方式。
/// </summary>
public enum WatermarkLayout
{
    /// <summary>單一置中</summary>
    Center,
    /// <summary>對角線單一(常見於"機密"字樣的斜向大字)</summary>
    DiagonalSingle,
    /// <summary>滿版重複貼(平舖),適合防拍照外流的場景</summary>
    TiledRepeat
}

/// <summary>
/// 浮水印內容類型:文字或圖片(Logo)。
/// </summary>
public enum WatermarkType
{
    Text,
    Image
}

/// <summary>
/// 浮水印參數。同時支援文字與圖片兩種內容類型,依 Type 決定實際使用哪一組欄位。
/// </summary>
public class WatermarkOptions
{
    /// <summary>浮水印內容類型,預設文字</summary>
    public WatermarkType Type { get; set; } = WatermarkType.Text;

    // ---------- 文字浮水印參數(Type = Text 時使用) ----------

    /// <summary>浮水印文字內容,例如公司名稱或"機密-僅供內部使用"</summary>
    public string Text { get; set; } = "機密文件";

    /// <summary>字型大小(pt)</summary>
    public double FontSize { get; set; } = 36;

    /// <summary>字型名稱</summary>
    public string FontName { get; set; } = "Microsoft JhengHei";

    /// <summary>文字顏色,預設灰色以避免過度遮擋內容</summary>
    public byte ColorR { get; set; } = 128;
    public byte ColorG { get; set; } = 128;
    public byte ColorB { get; set; } = 128;

    // ---------- 圖片浮水印參數(Type = Image 時使用) ----------

    /// <summary>浮水印圖片的本機檔案路徑(PNG/JPG等常見格式皆可)</summary>
    public string? ImagePath { get; set; }

    /// <summary>圖片繪製寬度(pt),高度依原始圖片長寬比例自動計算</summary>
    public double ImageWidthPoints { get; set; } = 150;

    // ---------- 共用參數 ----------

    /// <summary>
    /// 透明度,範圍 0.0(完全透明,幾乎看不見) ~ 1.0(完全不透明)。
    /// 文字與圖片皆適用;圖片會將透明度烘進圖片本身的alpha色版後再嵌入PDF。
    /// 一般浮水印建議 0.15 ~ 0.35 之間,兼顧可讀性與不遮擋原內容。
    /// </summary>
    public double Opacity { get; set; } = 0.25;

    /// <summary>旋轉角度(度),例如45表示由左下往右上的斜向文字/圖片</summary>
    public double RotationDegrees { get; set; } = 45;

    /// <summary>排版方式</summary>
    public WatermarkLayout Layout { get; set; } = WatermarkLayout.DiagonalSingle;

    /// <summary>
    /// 平舖模式下,浮水印之間的水平/垂直間距(pt)。
    /// 數值愈小,貼的密度愈高。僅在 Layout = TiledRepeat 時生效。
    /// </summary>
    public double TileSpacingX { get; set; } = 220;
    public double TileSpacingY { get; set; } = 160;

    /// <summary>
    /// 起始位置的水平/垂直偏移量(pt),以頁面正中央為基準點(0,0)。
    /// 正值:X往右、Y往下;負值反向。用來讓浮水印不要固定貼在正中央,
    /// 例如偏移到接近右下角的位置。
    /// 平舖模式下,偏移量會整個套用在貼圖網格的起始點,可用來微調網格對齊位置。
    /// </summary>
    public double OffsetXPoints { get; set; } = 0;
    public double OffsetYPoints { get; set; } = 0;

    public void Validate()
    {
        if (Opacity < 0.0 || Opacity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(Opacity), "透明度必須介於0.0與1.0之間");

        if (Type == WatermarkType.Text)
        {
            if (string.IsNullOrWhiteSpace(Text))
                throw new ArgumentException("浮水印文字不可為空");
            if (FontSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(FontSize), "字型大小必須大於0");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ImagePath))
                throw new ArgumentException("浮水印圖片路徑不可為空");
            if (ImageWidthPoints <= 0)
                throw new ArgumentOutOfRangeException(nameof(ImageWidthPoints), "圖片寬度必須大於0");
        }

        if (Layout == WatermarkLayout.TiledRepeat && (TileSpacingX <= 0 || TileSpacingY <= 0))
            throw new ArgumentOutOfRangeException(nameof(TileSpacingX), "平舖間距必須大於0");
    }
}
