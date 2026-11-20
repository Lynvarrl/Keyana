using System.IO;
using System.Windows;
using System.Windows.Controls;
using Keyana.Core;
using Keyana.Storage;
using Microsoft.Win32;

namespace Keyana.App;

/// <summary>
/// 键盘配列的装载与切换。
///
/// 配列只影响热力图的画法：采集、入库、排行榜、类别占比都按物理键（KeyId）走，
/// 换一套 60% 配列不会让任何一个数字发生变化，只是把没画出来的键从图上省掉。
/// </summary>
public partial class MainWindow
{
    private LayoutLibrary? _layoutLibrary;
    private bool _layoutUiUpdating;

    private void InitializeLayouts()
    {
        // Data 目录与数据库同一层：复制整个程序文件夹即可把自定义配列一起带走。
        _layoutLibrary = new LayoutLibrary(Path.Combine(AppContext.BaseDirectory, "Data"));

        PopulateLayoutChoices();
        ApplyLayout(KeyLayouts.Default, persist: false);
        UpdateLayoutPathText();
    }

    private void PopulateLayoutChoices()
    {
        if (_layoutLibrary is null)
        {
            return;
        }

        var previousId = (LayoutComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _layoutUiUpdating = true;
        try
        {
            LayoutComboBox.Items.Clear();
            foreach (var layout in _layoutLibrary.Layouts)
            {
                LayoutComboBox.Items.Add(new ComboBoxItem
                {
                    Content = layout.Name,
                    Tag = layout.Id,
                });
            }

            SelectLayoutItem(previousId);
        }
        finally
        {
            _layoutUiUpdating = false;
        }
    }

    private void SelectLayoutItem(string? id)
    {
        var target = id ?? KeyLayouts.DefaultId;
        foreach (var item in LayoutComboBox.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } &&
                string.Equals(tag, target, StringComparison.OrdinalIgnoreCase))
            {
                LayoutComboBox.SelectedItem = item;
                return;
            }
        }

        LayoutComboBox.SelectedIndex = 0;
    }

    /// <summary>切换配列：重画热力图并更新卡片上的说明文字。</summary>
    private void ApplyLayout(KeyLayout layout, bool persist)
    {
        HeatmapKeyboard.SetLayout(layout);
        RenderHeatmap(_counter.GetSnapshot().PressedKeys);
        LayoutHintText.Text = DescribeLayout(layout, _selectedRawCounts);

        // 下拉框要跟着走：从设置恢复、导入后自动切换，都要反映到界面上。
        _layoutUiUpdating = true;
        try
        {
            SelectLayoutItem(layout.Id);
        }
        finally
        {
            _layoutUiUpdating = false;
        }

        if (!persist || _settings is null)
        {
            return;
        }

        _settings.KeyboardLayoutId = layout.Id;
        _ = SaveSettingsAsync();
    }

    /// <summary>
    /// 配列说明。只说「这套配列没画出来、但你确实有输入」的键——
    /// 按「一共少画了多少键」报数没有意义：104 也不会画 F13-F24、浏览器键和多媒体键，
    /// 那样每套配列都会挂一句警告。鼠标按键总画在键盘下方，不参与计算。
    /// </summary>
    private static string DescribeLayout(KeyLayout layout, IReadOnlyList<ulong> counts)
    {
        var hiddenWithData = 0;
        foreach (var definition in KeyCatalog.Definitions)
        {
            if (HeatBasis.IsMouseKey(definition.Id) || layout.KeyIds.Contains(definition.Id))
            {
                continue;
            }

            var index = (int)definition.Id;
            if (index < counts.Count && counts[index] > 0)
            {
                hiddenWithData++;
            }
        }

        return hiddenWithData == 0
            ? layout.Name
            : $"{layout.Name} · {hiddenWithData} 个未显示的键有输入（仍可在排行榜查看）";
    }

    private void UpdateLayoutPathText()
    {
        if (_layoutLibrary is null)
        {
            return;
        }

        LayoutPathText.Text = $"自定义配列目录：{_layoutLibrary.DirectoryPath}";
        LayoutPathText.ToolTip = _layoutLibrary.LoadErrors.Count == 0
            ? "把配列 JSON 放在这个目录下即可被发现；也可以在右侧点「导入布局…」。"
            : string.Join(Environment.NewLine, _layoutLibrary.LoadErrors);
    }

    private void LayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_layoutUiUpdating || !_uiReady || _layoutLibrary is null)
        {
            return;
        }

        if ((LayoutComboBox.SelectedItem as ComboBoxItem)?.Tag is not string id ||
            _layoutLibrary.Find(id) is not { } layout)
        {
            return;
        }

        ApplyLayout(layout, persist: true);
    }

    private void ImportLayoutButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_layoutLibrary is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入键盘配列",
            Filter = "配列文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!_layoutLibrary.TryImport(dialog.FileName, out var layout, out var error) || layout is null)
        {
            MessageBox.Show(
                this,
                $"这套配列无法导入：\n\n{error}",
                "导入配列失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        PopulateLayoutChoices();
        ApplyLayout(layout, persist: true);
        UpdateLayoutPathText();
    }
}
