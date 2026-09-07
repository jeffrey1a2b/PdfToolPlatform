using System.Windows;
using PdfSharp.Fonts;
using PdfToolPlatform.Core.Fonts;

namespace PdfToolPlatform.UI;

public partial class App : Application
{
    public App()
    {
        // 與Web版共用同一個字型解析器,避免文字浮水印功能拋出找不到字型的例外
        GlobalFontSettings.FontResolver = new WindowsFontResolver();
    }
}
