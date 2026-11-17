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
        catch (Exception) when (!_isClosing)
        {
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

        if (_activeRangePreset == TimeRangePreset.Custom)
        {
            _ = ApplyActivePresetAsync();
        }
    }

    private void InitializeStartupSetting()
    {
        _startupSettingChanging = true;
        try
        {
            StartupCheckBox.IsChecked = _startupRegistration.IsEnabledForCurrentExecutable();
        }
        catch (Exception exception)
        {
            StartupCheckBox.IsChecked = false;
            StartupCheckBox.ToolTip = $"读取启动项失败：{exception.Message}";
        }
        finally
        {
            _startupSettingChanging = false;
        }
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_startupSettingChanging)
        {
            return;
        }

        var requestedState = StartupCheckBox.IsChecked == true;
        try
        {
            _startupRegistration.SetEnabled(requestedState);
            StartupCheckBox.ToolTip = requestedState
                ? "已为当前 Windows 用户启用；程序将从当前绿色版路径静默启动。"
                : "未启用开机自启动。";
        }
        catch (Exception exception)
        {
            _startupSettingChanging = true;
            StartupCheckBox.IsChecked = !requestedState;
            _startupSettingChanging = false;
            MessageBox.Show(
                this,
                $"无法修改 Windows 启动项。\n\n{exception.Message}",
                "Keyana",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
