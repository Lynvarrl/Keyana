using System.Drawing;

namespace Keyana.App;

internal sealed class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _pauseItem;
    private readonly Icon? _applicationIcon;
    private bool _hiddenNotificationShown;
    private bool _isPaused;
    private string? _errorText;

    public TrayIconService(Action open, Action togglePause, Action saveNow, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("打开统计窗口");
        _pauseItem = new System.Windows.Forms.ToolStripMenuItem("暂停采集");
        var saveItem = new System.Windows.Forms.ToolStripMenuItem("立即保存");
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("完整退出");

        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => open();
        _pauseItem.Click += (_, _) => togglePause();
        saveItem.Click += (_, _) => saveNow();
        exitItem.Click += (_, _) => exit();

        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(saveItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _applicationIcon = Environment.ProcessPath is { } executablePath
            ? Icon.ExtractAssociatedIcon(executablePath)
            : null;
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? SystemIcons.Application,
            Text = "Keyana · 正在采集",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => open();
    }

    public void SetPaused(bool isPaused)
    {
        _isPaused = isPaused;
        _pauseItem.Text = isPaused ? "恢复采集" : "暂停采集";
        UpdateTooltip();
    }

    /// <summary>显示/清除当前故障（保存失败、存储不可用）。null 表示恢复正常。</summary>
    public void SetError(string? message)
    {
        if (string.Equals(_errorText, message, StringComparison.Ordinal))
        {
            return;
        }

        _errorText = message;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        // NotifyIcon.Text 上限 63 字符，这里只放短状态，详细原因留在主窗口。
        _notifyIcon.Text = _errorText is { Length: > 0 } error
            ? $"Keyana · {error}"
            : _isPaused ? "Keyana · 已暂停" : "Keyana · 正在采集";
    }

    public void ShowHiddenNotification()
    {
        if (_hiddenNotificationShown)
        {
            return;
        }

        _hiddenNotificationShown = true;
        _notifyIcon.BalloonTipTitle = "Keyana 仍在后台采集";
        _notifyIcon.BalloonTipText = "双击托盘图标可重新打开；选择“完整退出”才会停止采集。";
        _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon?.Dispose();
    }
}
