using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Keyana.Core;

namespace Keyana.App.Controls;

/// <summary>
/// 按键筛选面板：按类别分组的多选列表 + 搜索，用作「只看这几个键」的分析入口。
///
/// 选择规则：不选任何键、或把全部键都选上，都等价于「不筛选」。
/// 这样按钮上的文字永远能明确回答「现在到底有没筛」，也不会出现「筛选了 132 个键」。
/// </summary>
public partial class KeyFilterPanel : UserControl
{
    private readonly ObservableCollection<KeyFilterItem> _items = [];
    private readonly ICollectionView _view;
    private IReadOnlySet<KeyId>? _selection;
    private bool _updating;

    public KeyFilterPanel()
    {
        InitializeComponent();

        foreach (var definition in KeyCatalog.Definitions)
        {
            _items.Add(new KeyFilterItem(definition, CategoryOrder(definition.Category)));
        }

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.SortDescriptions.Add(new SortDescription(nameof(KeyFilterItem.CategoryOrder), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(KeyFilterItem.DisplayOrder), ListSortDirection.Ascending));
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(KeyFilterItem.Category)));
        KeyList.ItemsSource = _view;

        foreach (var item in _items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        UpdateSummary();
    }

    /// <summary>筛选集合变化（用户勾选、全选、清空、取消）。值为 null 表示未筛选。</summary>
    public event EventHandler? FilterChanged;

    /// <summary>用户点了「同步到另一页」。</summary>
    public event EventHandler? SyncRequested;

    /// <summary>当前筛选；null 表示未筛选（等价于全部按键）。</summary>
    public IReadOnlySet<KeyId>? Selection => _selection;

    /// <summary>同步按钮上的目标页名，由宿主页面设置。</summary>
    public string SyncTarget
    {
        get => SyncButton.Content as string ?? string.Empty;
        set => SyncButton.Content = value;
    }

    /// <summary>用当前区间的计数刷新每个键后面的次数，方便挑「真正用得多的键」。</summary>
    public void SetCounts(IReadOnlyList<ulong> counts)
    {
        foreach (var item in _items)
        {
            var index = (int)item.Id;
            item.Count = (uint)index < (uint)counts.Count ? counts[index] : 0UL;
        }

        UpdateSummary();
    }

    /// <summary>从外部同步筛选（另一页同步过来、或宿主恢复状态）。</summary>
    public void SetSelection(IReadOnlySet<KeyId>? selection)
    {
        _updating = true;
        try
        {
            // 先全部清掉再按集合勾选：外部传入的集合可能只含部分键。
            foreach (var item in _items)
            {
                item.IsSelected = selection is not null && selection.Contains(item.Id);
            }
        }
        finally
        {
            _updating = false;
        }

        _selection = Normalize(selection);
        UpdateSummary();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(KeyFilterItem.IsSelected) || _updating)
        {
            return;
        }

        PublishSelection();
    }

    private void PublishSelection()
    {
        var selected = _items.Where(item => item.IsSelected).Select(item => item.Id).ToHashSet();
        var normalized = Normalize(selected);
        if (SetEquals(normalized, _selection))
        {
            UpdateSummary();
            return;
        }

        _selection = normalized;
        UpdateSummary();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>未选中任何键、或全选，都当作未筛选。</summary>
    private IReadOnlySet<KeyId>? Normalize(IReadOnlySet<KeyId>? selection)
    {
        if (selection is null || selection.Count == 0)
        {
            return null;
        }

        return selection.Count >= _items.Count ? null : selection;
    }

    private static bool SetEquals(IReadOnlySet<KeyId>? left, IReadOnlySet<KeyId>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SetEquals(right);
    }

    private void UpdateSummary()
    {
        var count = _selection?.Count ?? 0;
        SummaryText.Text = count == 0 ? "全部按键" : $"已选 {count} 个键";
        ResetButton.IsEnabled = count > 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        var keyword = SearchBox.Text.Trim();
        SearchHint.Visibility = keyword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (keyword.Length == 0)
        {
            _view.Filter = null;
            return;
        }

        _view.Filter = item => item is KeyFilterItem key &&
                               (key.DisplayName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                                key.Category.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        // 只对当前搜索结果显示的键生效：搜「shift」再点全选，不应该把整张表都勾上。
        SetBulk(selected: true);
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs eventArgs) => SetBulk(selected: false);

    private void SetBulk(bool selected)
    {
        _updating = true;
        try
        {
            foreach (var item in _view.Cast<KeyFilterItem>())
            {
                item.IsSelected = selected;
            }
        }
        finally
        {
            _updating = false;
        }

        PublishSelection();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs eventArgs) => SetBulk(selected: false);

    private void SyncButton_Click(object sender, RoutedEventArgs eventArgs) =>
        SyncRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>分组顺序与「类别占比」保持一致，不随字母序乱跳。</summary>
    private static int CategoryOrder(string category) => category switch
    {
        "主键区" => 0,
        "功能键" => 1,
        "导航键" => 2,
        "数字键盘" => 3,
        "鼠标" => 4,
        _ => 5,
    };
}

/// <summary>筛选列表的一行。Count 变化频繁，单独做成属性让界面只刷新这一格。</summary>
public sealed class KeyFilterItem : INotifyPropertyChanged
{
    private ulong _count;
    private bool _isSelected;

    public KeyFilterItem(KeyDefinition definition, int categoryOrder)
    {
        Id = definition.Id;
        DisplayName = definition.DisplayName;
        Category = definition.Category;
        DisplayOrder = definition.DisplayOrder;
        CategoryOrder = categoryOrder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KeyId Id { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public int DisplayOrder { get; }

    public int CategoryOrder { get; }

    public ulong Count
    {
        get => _count;
        set
        {
            if (_count == value)
            {
                return;
            }

            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        }
    }

    public string CountText => _count.ToString("N0");

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
