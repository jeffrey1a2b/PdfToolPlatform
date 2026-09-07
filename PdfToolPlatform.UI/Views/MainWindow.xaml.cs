using System.Windows;
using Microsoft.Win32;
using PdfToolPlatform.Core.Models;
using PdfToolPlatform.Core.Services;

namespace PdfToolPlatform.UI.Views;

public partial class MainWindow : Window
{
    private readonly PdfMergeService _mergeService = new();
    private readonly PdfSplitService _splitService = new();
    private readonly WatermarkService _watermarkService = new();
    private readonly PdfToImageService _toImageService = new();

    private string? _splitSelectedFile;
    private string? _watermarkSelectedFile;
    private string? _toImageSelectedFile;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ---------- 合併 ----------

    private void OnMergeAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF 檔案 (*.pdf)|*.pdf",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                MergeFileList.Items.Add(file);
        }
    }

    private void OnMergeMoveUp(object sender, RoutedEventArgs e) => MoveSelected(MergeFileList, -1);
    private void OnMergeMoveDown(object sender, RoutedEventArgs e) => MoveSelected(MergeFileList, 1);

    private void OnMergeRemove(object sender, RoutedEventArgs e)
    {
        if (MergeFileList.SelectedItem is string item)
            MergeFileList.Items.Remove(item);
    }

    private static void MoveSelected(System.Windows.Controls.ListBox listBox, int direction)
    {
        int index = listBox.SelectedIndex;
        if (index < 0) return;
        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= listBox.Items.Count) return;

        var item = listBox.Items[index];
        listBox.Items.RemoveAt(index);
        listBox.Items.Insert(newIndex, item);
        listBox.SelectedIndex = newIndex;
    }

    private void OnMergeExecute(object sender, RoutedEventArgs e)
    {
        if (MergeFileList.Items.Count == 0)
        {
            MergeStatus.Foreground = System.Windows.Media.Brushes.Red;
            MergeStatus.Text = "請先加入至少一個PDF檔案。";
            return;
        }

        var saveDialog = new SaveFileDialog { Filter = "PDF 檔案 (*.pdf)|*.pdf", FileName = "merged.pdf" };
        if (saveDialog.ShowDialog() != true) return;

        try
        {
            var paths = MergeFileList.Items.Cast<string>().ToList();
            _mergeService.Merge(paths, saveDialog.FileName);
            MergeStatus.Foreground = System.Windows.Media.Brushes.Green;
            MergeStatus.Text = $"完成,已儲存至: {saveDialog.FileName}";
        }
        catch (Exception ex)
        {
            MergeStatus.Foreground = System.Windows.Media.Brushes.Red;
            MergeStatus.Text = $"失敗: {ex.Message}";
        }
    }

    // ---------- 分割 ----------

    private void OnSplitSelectFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF 檔案 (*.pdf)|*.pdf" };
        if (dialog.ShowDialog() == true)
        {
            _splitSelectedFile = dialog.FileName;
            SplitFileLabel.Text = dialog.FileName;
        }
    }

    private void OnSplitExecute(object sender, RoutedEventArgs e)
    {
        if (_splitSelectedFile is null)
        {
            SplitStatus.Foreground = System.Windows.Media.Brushes.Red;
            SplitStatus.Text = "請先選擇PDF檔案。";
            return;
        }

        var folderDialog = new OpenFolderDialog { Title = "選擇輸出資料夾" };
        if (folderDialog.ShowDialog() != true) return;

        try
        {
            List<string> outputs;
            if (SplitModeFixed.IsChecked == true)
            {
                int pagesPerFile = int.Parse(SplitPagesPerFile.Text.Trim());
                outputs = _splitService.SplitByFixedSize(_splitSelectedFile, folderDialog.FolderName, pagesPerFile);
            }
            else
            {
                var ranges = ParseRanges(SplitRanges.Text);
                outputs = _splitService.SplitByMultipleRanges(_splitSelectedFile, folderDialog.FolderName, ranges);
            }

            SplitStatus.Foreground = System.Windows.Media.Brushes.Green;
            SplitStatus.Text = $"完成,共產出 {outputs.Count} 個檔案於: {folderDialog.FolderName}";
        }
        catch (Exception ex)
        {
            SplitStatus.Foreground = System.Windows.Media.Brushes.Red;
            SplitStatus.Text = $"失敗: {ex.Message}";
        }
    }

    private static IEnumerable<(int start, int end)> ParseRanges(string text)
    {
        // 格式範例: "1-3,4-10,11-12"
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('-', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || !int.TryParse(pieces[0], out var start) || !int.TryParse(pieces[1], out var end))
                throw new FormatException($"範圍格式錯誤: \"{part}\",請使用如 1-3 的格式,多組範圍以逗號分隔");
            yield return (start, end);
        }
    }

    // ---------- 浮水印 ----------

    private void OnWatermarkSelectFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF 檔案 (*.pdf)|*.pdf" };
        if (dialog.ShowDialog() == true)
        {
            _watermarkSelectedFile = dialog.FileName;
            WatermarkFileLabel.Text = dialog.FileName;
        }
    }

    private void OnWatermarkExecute(object sender, RoutedEventArgs e)
    {
        if (_watermarkSelectedFile is null)
        {
            WatermarkStatus.Foreground = System.Windows.Media.Brushes.Red;
            WatermarkStatus.Text = "請先選擇PDF檔案。";
            return;
        }

        var saveDialog = new SaveFileDialog { Filter = "PDF 檔案 (*.pdf)|*.pdf", FileName = "watermarked.pdf" };
        if (saveDialog.ShowDialog() != true) return;

        try
        {
            var layout = WatermarkLayoutCombo.SelectedIndex switch
            {
                0 => WatermarkLayout.Center,
                1 => WatermarkLayout.DiagonalSingle,
                2 => WatermarkLayout.TiledRepeat,
                _ => WatermarkLayout.DiagonalSingle
            };

            var options = new WatermarkOptions
            {
                Text = WatermarkText.Text,
                Opacity = WatermarkOpacity.Value,
                RotationDegrees = double.Parse(WatermarkRotation.Text.Trim()),
                Layout = layout,
                TileSpacingX = double.Parse(WatermarkSpacingX.Text.Trim()),
                TileSpacingY = double.Parse(WatermarkSpacingY.Text.Trim()),
                OffsetXPoints = double.Parse(WatermarkOffsetX.Text.Trim()),
                OffsetYPoints = double.Parse(WatermarkOffsetY.Text.Trim())
            };

            _watermarkService.ApplyWatermark(_watermarkSelectedFile, saveDialog.FileName, options);
            WatermarkStatus.Foreground = System.Windows.Media.Brushes.Green;
            WatermarkStatus.Text = $"完成,已儲存至: {saveDialog.FileName}";
        }
        catch (Exception ex)
        {
            WatermarkStatus.Foreground = System.Windows.Media.Brushes.Red;
            WatermarkStatus.Text = $"失敗: {ex.Message}";
        }
    }

    // ---------- 轉圖檔 ----------

    private void OnToImageSelectFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF 檔案 (*.pdf)|*.pdf" };
        if (dialog.ShowDialog() == true)
        {
            _toImageSelectedFile = dialog.FileName;
            ToImageFileLabel.Text = dialog.FileName;
        }
    }

    private void OnToImageExecute(object sender, RoutedEventArgs e)
    {
        if (_toImageSelectedFile is null)
        {
            ToImageStatus.Foreground = System.Windows.Media.Brushes.Red;
            ToImageStatus.Text = "請先選擇PDF檔案。";
            return;
        }

        var folderDialog = new OpenFolderDialog { Title = "選擇輸出資料夾" };
        if (folderDialog.ShowDialog() != true) return;

        try
        {
            int dpi = int.Parse(ToImageDpi.Text.Trim());
            var format = ToImageFormatCombo.SelectedIndex == 0 ? ImageFormat.Png : ImageFormat.Jpeg;

            var outputs = _toImageService.ConvertToImages(_toImageSelectedFile, folderDialog.FolderName, dpi, format);
            ToImageStatus.Foreground = System.Windows.Media.Brushes.Green;
            ToImageStatus.Text = $"完成,共產出 {outputs.Count} 張圖片於: {folderDialog.FolderName}";
        }
        catch (Exception ex)
        {
            ToImageStatus.Foreground = System.Windows.Media.Brushes.Red;
            ToImageStatus.Text = $"失敗: {ex.Message}";
        }
    }
}
