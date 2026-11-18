using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Keyana.App.Controls;
using Keyana.App.Theme;
using Keyana.Storage;

namespace Keyana.App;

public partial class MainWindow
{
    private async void Nav_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        OverviewPage.Visibility = tag == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        AnalysisPage.Visibility = tag == "Analysis" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.ScrollToTop();

        if (tag == "Analysis")
        {
            // 相对预设（今日/近7日/近30日）跟着当前时间走；自定义区间保持用户选的值。
            if (_analysisPreset != TimeRangePreset.Custom)
            {
                await SyncAnalysisRangeFromPresetAsync();
            }

            _ = RefreshAnalysisAsync(force: true);
        }
        else if (tag == "Settings")
        {
            _ = RefreshStorageUiAsync(force: true);
        }
    }

    private void PopulateFontFamilyChoices()
    {
        FontFamilyComboBox.Items.Clear();
        FontFamilyComboBox.Items.Add(new ComboBoxItem
        {
            Content = "系统默认（Segoe UI / 微软雅黑）",
            Tag = string.Empty,
        });

        foreach (var family in Fonts.SystemFontFamilies.OrderBy(family => family.Source, StringComparer.CurrentCulture))
        {
            FontFamilyComboBox.Items.Add(new ComboBoxItem
            {
                Content = family.Source,
                Tag = family.Source,
            });
        }

        FontFamilyComboBox.SelectedIndex = 0;
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        _settingsUiUpdating = true;
        try
        {
            _currentPalette = Enum.TryParse<HeatPalette>(settings.HeatPalette, out var palette)
                ? palette
                : HeatPalette.BlueMagenta;

            PaletteChipBlue.IsChecked = _currentPalette == HeatPalette.BlueMagenta;
            PaletteChipGreen.IsChecked = _currentPalette == HeatPalette.GreenAmber;

            ThemeChipSystem.IsChecked = settings.Theme == "System";
            ThemeChipLight.IsChecked = settings.Theme == "Light";
            ThemeChipDark.IsChecked = settings.Theme == "Dark";

            SnapCheckBox.IsChecked = settings.SnapToTenMinutes;
            SelectFontFamily(settings.FontFamily);
            SelectFontScale(settings.FontScale);
        }
        finally
        {
            _settingsUiUpdating = false;
        }

        ThemeManager.ApplyFromSettings(settings);
        UpdateGradientBar();
        RefreshStatusVisual();
        _paletteReady = true;
    }

    private void SelectFontFamily(string fontFamily)
    {
        for (var index = 0; index < FontFamilyComboBox.Items.Count; index++)
        {
            if (FontFamilyComboBox.Items[index] is ComboBoxItem { Tag: string tag } &&
                string.Equals(tag, fontFamily, StringComparison.OrdinalIgnoreCase))
            {
                FontFamilyComboBox.SelectedIndex = index;
                return;
            }
        }

        FontFamilyComboBox.SelectedIndex = 0;
    }

    private void SelectFontScale(double fontScale)
    {
        RadioButton? best = null;
        var bestDistance = double.MaxValue;
        foreach (var chip in new[]
                 {
                     FontScaleChipCompact, FontScaleChipStandard, FontScaleChipLarge, FontScaleChipXLarge,
                 })
        {
            if (chip.Tag is not string tag ||
                !double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var distance = Math.Abs(value - fontScale);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = chip;
            }
        }

        if (best is not null)
        {
            best.IsChecked = true;
        }
    }

    private void ThemeChip_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || _settingsUiUpdating || _settings is null ||
            sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        _settings.Theme = tag;
        ApplyAppearance();
    }

    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_uiReady || _settingsUiUpdating || _settings is null)
        {
            return;
        }

        var selected = (FontFamilyComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
        if (string.Equals(_settings.FontFamily, selected, StringComparison.Ordinal))
        {
            return;
        }

        _settings.FontFamily = selected;
        ApplyAppearance();
    }

    private void FontScaleChip_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || _settingsUiUpdating || _settings is null ||
            sender is not RadioButton { Tag: string tag } ||
            !double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
        {
            return;
        }

        if (Math.Abs(_settings.FontScale - scale) < 0.001)
        {
            return;
        }

        _settings.FontScale = scale;
        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        if (_settings is null)
        {
            return;
        }

        ThemeManager.ApplyFromSettings(_settings);
        ThemeManager.ApplyWindowChrome(this);
        UpdateGradientBar();
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        if (_settingsStore is null || _settings is null)
        {
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(_settings, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // 退出/取消不算保存失败。
        }
        catch (Exception exception) when (!_isClosing)
        {
            // 设置保存失败以前完全静默：用户改了主题/吸附，下次启动却回到旧值。
            ErrorLog.Record("Settings.Save", exception);
        }
    }

    private void SnapCheckBox_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (!_uiReady || _settingsUiUpdating || _settings is null)
        {
            return;
        }

        _settings.SnapToTenMinutes = SnapCheckBox.IsChecked == true;
        _ = SaveSettingsAsync();

        // 三个自定义区间都必须从原始输入重算，不能只改主范围。
        if (_activeRangePreset == TimeRangePreset.Custom)
        {
            _ = ApplyActivePresetAsync();
        }

        if (_activityPreset == TimeRangePreset.Custom)
        {
            _ = ApplyActivityCustomRangeAsync();
        }

        if (_analysisPreset == TimeRangePreset.Custom)
        {
            _ = ApplyAnalysisCustomRangeAsync();
        }
    }

    private void InitializeStartupSetting()
    {
        try
        {
            ApplyStartupState(_startupRegistration.GetRegistrationState());
        }
        catch (Exception exception)
        {
            _startupSettingChanging = true;
            try
            {
                StartupCheckBox.IsChecked = false;
                StartupAdminCheckBox.IsChecked = false;
                StartupAdminCheckBox.IsEnabled = false;
            }
            finally
            {
                _startupSettingChanging = false;
            }

            StartupCheckBox.ToolTip = $"读取启动项失败：{exception.Message}";
        }
    }

    /// <summary>按任务的真实状态回填两个勾选框；调用期间抑制 Checked 事件。</summary>
    private void ApplyStartupState(StartupRegistrationState state)
    {
        _startupSettingChanging = true;
        try
        {
            StartupCheckBox.IsChecked = state.IsRegistered;
            StartupAdminCheckBox.IsChecked = state.IsRegistered && state.RunAsAdministrator;
            // 不根据当前令牌是否含管理员组来禁用：UAC 提权的是创建任务的 helper，
            // 不是主程序；普通权限的主程序完全有权发起这次提权。是否真的能提权，
            // 由用户点选后的 UAC 结果决定，失败会回滚并提示。
            StartupAdminCheckBox.IsEnabled = state.IsRegistered;
        }
        finally
        {
            _startupSettingChanging = false;
        }

        StartupCheckBox.ToolTip = state.IsRegistered
            ? "已为当前 Windows 用户启用；程序将从当前绿色版路径静默启动。"
            : "未启用开机自启动。";
        StartupAdminCheckBox.ToolTip = !state.IsRegistered
            ? "先启用开机自启，再选择是否以管理员权限运行。"
            : state.RunAsAdministrator
                ? "当前以管理员权限自启。取消勾选会改回普通权限（不需要 UAC）。"
                : "当前以普通权限自启。勾选后会弹一次 UAC；需要当前 Windows 账户属于管理员组，否则登录后仍以普通权限运行。";

        if (state.IsRegistered && !state.MatchesCurrentDefinition)
        {
            StartupCheckBox.ToolTip =
                "已启用，但任务仍是旧策略（例如缺少电池/运行时限设置）。取消勾选再重新勾选一次即可重建。";
        }
    }

    private async void StartupCheckBox_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_startupSettingChanging)
        {
            return;
        }

        var requestedState = StartupCheckBox.IsChecked == true;
        var runAsAdministrator = requestedState && StartupAdminCheckBox.IsChecked == true;
        await ChangeStartupRegistrationAsync(requestedState, runAsAdministrator);
    }

    private async void StartupAdminCheckBox_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_startupSettingChanging || StartupCheckBox.IsChecked != true)
        {
            return;
        }

        await ChangeStartupRegistrationAsync(enabled: true, StartupAdminCheckBox.IsChecked == true);
    }

    private async Task ChangeStartupRegistrationAsync(bool enabled, bool runAsAdministrator)
    {
        // 建任务可能要等 UAC，删任务要跑 schtasks：都不能阻塞 UI 线程。
        StartupCheckBox.IsEnabled = false;
        StartupAdminCheckBox.IsEnabled = false;

        Exception? failure = null;
        try
        {
            await Task.Run(() => _startupRegistration.SetEnabled(enabled, runAsAdministrator));
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // 无论成功失败都按任务真实状态回填，不留下“勾了但没建/没删”的假象。
        try
        {
            ApplyStartupState(await Task.Run(_startupRegistration.GetRegistrationState));
        }
        catch
        {
            // 回填失败时保持现状，错误已经足以提示用户。
        }

        StartupCheckBox.IsEnabled = true;
        StartupAdminCheckBox.IsEnabled = StartupCheckBox.IsChecked == true;

        if (failure is not null)
        {
            ErrorLog.Record("Startup", failure);
            MessageBox.Show(
                this,
                $"无法修改 Windows 启动项。\n\n{failure.Message}",
                "Keyana",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
