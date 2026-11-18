using System.Windows;
using System.Windows.Controls;

namespace Keyana.App.Controls;

/// <summary>
/// 统计区间选择器：一个按钮 + 弹出层，弹出层里是「点两次确定起止日期」的月历，
/// 以及两个可滚轮微调的时间。
///
/// 时间的默认口径：结束时间取当前时刻，开始时间取所选起始日的零点——
/// 这是查询历史区间时最常用的组合，需要精确到分钟时再用滚轮改。
/// </summary>
public partial class DateRangePicker : UserControl
{
    private DateTime _rangeStart = DateTime.Today;
    private DateTime _rangeEnd = TruncateToMinute(DateTime.Now);

    public DateRangePicker()
    {
        InitializeComponent();

        CalendarControl.RangeSelected += CalendarControl_RangeSelected;
        StartTime.ValueChanged += (_, _) => SyncValidation();
        EndTime.ValueChanged += (_, _) => SyncValidation();
        UpdateDisplayText();
    }

    /// <summary>用户点击「应用」后触发；由外部重新查询数据。</summary>
    public event EventHandler? RangeApplied;

    public DateTime RangeStart => _rangeStart;

    public DateTime RangeEnd => _rangeEnd;

    /// <summary>弹出层是否打开。外部定时同步区间时应跳过，避免覆盖用户正在选的值。</summary>
    public bool IsEditorOpen => EditorPopup.IsOpen;

    public void SetRange(DateTime start, DateTime end)
    {
        _rangeStart = start;
        _rangeEnd = end;
        UpdateDisplayText();
        if (EditorPopup.IsOpen)
        {
            LoadPendingIntoEditors();
        }
    }

    public void SetRangeUtc(long startUtc, long endUtc) =>
        SetRange(
            DateTimeOffset.FromUnixTimeSeconds(startUtc).ToLocalTime().DateTime,
            DateTimeOffset.FromUnixTimeSeconds(endUtc).ToLocalTime().DateTime);

    /// <summary>从外部（例如「自定义」胶囊）直接展开选择器。</summary>
    public void Open()
    {
        LoadPendingIntoEditors();
        ValidationText.Visibility = Visibility.Collapsed;
        EditorPopup.IsOpen = true;
    }

    private void DisplayButton_Click(object sender, RoutedEventArgs eventArgs) => Open();

    private void LoadPendingIntoEditors()
    {
        CalendarControl.SetRange(
            DateOnly.FromDateTime(_rangeStart),
            DateOnly.FromDateTime(_rangeEnd));
        StartTime.SetValueSilently(_rangeStart.TimeOfDay);
        EndTime.SetValueSilently(_rangeEnd.TimeOfDay);
    }

    private void CalendarControl_RangeSelected(object? sender, EventArgs eventArgs)
    {
        if (CalendarControl.StartDate is not { } startDate || CalendarControl.EndDate is not { } endDate)
        {
            SyncValidation();
            return;
        }

        // 同一天点两次、时间又恰好不小于终点时，把区间补成完整的一天。
        // 否则用户会得到「09:00 → 09:00」这种必然被拒绝的组合，还得自己找原因。
        if (startDate == endDate && StartTime.Value >= EndTime.Value)
        {
            StartTime.SetValueSilently(TimeSpan.Zero);
            if (EndTime.Value <= TimeSpan.Zero)
            {
                EndTime.SetValueSilently(new TimeSpan(23, 59, 0));
            }
        }

        SyncValidation();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryCompose(out var start, out var end))
        {
            SyncValidation();
            return;
        }

        _rangeStart = start;
        _rangeEnd = end;
        UpdateDisplayText();
        EditorPopup.IsOpen = false;
        RangeApplied?.Invoke(this, EventArgs.Empty);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs eventArgs) => EditorPopup.IsOpen = false;

    private bool TryCompose(out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        if (CalendarControl.StartDate is not { } startDate || CalendarControl.EndDate is not { } endDate)
        {
            return false;
        }

        start = startDate.ToDateTime(TimeOnly.FromTimeSpan(StartTime.Value));
        end = endDate.ToDateTime(TimeOnly.FromTimeSpan(EndTime.Value));

        // 区间是左闭右开的：单日选到 23:59 的真实含义是「到当天结束」，补成次日
        // 00:00；否则 23:59 这一分钟会被右开区间漏掉。24:00 无法直接输入，这是
        // 唯一能表达「完整一天」的写法。
        if (startDate == endDate && TimeOnly.FromTimeSpan(EndTime.Value) == new TimeOnly(23, 59))
        {
            end = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }

        return end > start;
    }

    private void SyncValidation()
    {
        if (CalendarControl.EndDate is null)
        {
            ShowValidation("再点一个日期确定终点。");
            return;
        }

        if (!TryCompose(out _, out _))
        {
            ShowValidation("结束时间必须晚于开始时间。");
            return;
        }

        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void UpdateDisplayText() =>
        RangeText.Text = $"{_rangeStart:yyyy-MM-dd HH:mm}  →  {_rangeEnd:yyyy-MM-dd HH:mm}";

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
}
