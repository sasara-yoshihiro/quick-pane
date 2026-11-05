using CommunityToolkit.Mvvm.ComponentModel;
using QuickPane.Core.Configuration;
using QuickPane.Core.Models;

namespace QuickPane.ViewModels;

/// <summary>
/// 設定画面。変更は即時反映・保存する。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly bool _initialized;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    public SettingsViewModel()
    {
        var settings = App.Current.Settings;
        HotkeyGesture.TryParse(settings.Hotkey, out var gesture);
        Hotkey = gesture;
        PositionIndex = (int)settings.Position;
        ThemeIndex = (int)settings.Theme;
        IconSizeIndex = (int)settings.IconSize;
        Columns = settings.Columns;
        CloseAfterLaunch = settings.CloseAfterLaunch;
        AutoStart = AutoStartManager.IsEnabled();
        HotkeyError = App.Current.Hotkey.IsRegistered ? string.Empty : "このホットキーは他のアプリで使用されているため登録できませんでした。別の組み合わせを設定してください。";
        _initialized = true;
    }

    /// <summary>ホットキー</summary>
    [ObservableProperty]
    public partial HotkeyGesture Hotkey { get; set; }

    /// <summary>ホットキーのエラーメッセージ</summary>
    [ObservableProperty]
    public partial string HotkeyError { get; set; }

    /// <summary>表示位置 (0: マウスカーソル付近、1: 画面中央)</summary>
    [ObservableProperty]
    public partial int PositionIndex { get; set; }

    /// <summary>テーマ (0: システム設定に従う、1: ライト、2: ダーク)</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>アイコンサイズ (0: 小、1: 中、2: 大)</summary>
    [ObservableProperty]
    public partial int IconSizeIndex { get; set; }

    /// <summary>1 行あたりのアイテム数</summary>
    [ObservableProperty]
    public partial double Columns { get; set; }

    /// <summary>アイテム起動後にランチャーを閉じる</summary>
    [ObservableProperty]
    public partial bool CloseAfterLaunch { get; set; }

    /// <summary>Windows サインイン時に自動起動</summary>
    [ObservableProperty]
    public partial bool AutoStart { get; set; }

    /// <summary>列数の最小値</summary>
    public double MinColumns => UserSettings.MinColumns;

    /// <summary>列数の最大値</summary>
    public double MaxColumns => UserSettings.MaxColumns;

    /// <summary>バージョン</summary>
    public string Version => $"バージョン {App.Version}";

    /// <summary>データフォルダ</summary>
    public string DataDirectory => AppPaths.DataDirectory;

    /// <summary>
    /// ホットキーを変更する。登録できない場合はエラーメッセージを設定し、元のホットキーのままとする。
    /// </summary>
    /// <param name="gesture">新しいホットキー</param>
    /// <returns>変更できた場合は true</returns>
    public bool TryChangeHotkey(HotkeyGesture gesture)
    {
        if (!App.Current.Hotkey.TryChange(gesture))
        {
            HotkeyError = $"{gesture.ToDisplayString()} は他のアプリまたは Windows で使用されているため登録できません。別の組み合わせを入力してください。";
            return false;
        }

        HotkeyError = string.Empty;
        Hotkey = gesture;
        App.Current.UpdateSettings(s => s.Hotkey = gesture.ToString());
        return true;
    }

    partial void OnPositionIndexChanged(int value) =>
        Update(s => s.Position = (LauncherPosition)value);

    partial void OnThemeIndexChanged(int value) =>
        Update(s => s.Theme = (AppTheme)value);

    partial void OnIconSizeIndexChanged(int value) =>
        Update(s => s.IconSize = (IconSizeOption)value);

    partial void OnColumnsChanged(double value)
    {
        if (!double.IsNaN(value))
        {
            Update(s => s.Columns = (int)Math.Round(value));
        }
    }

    partial void OnCloseAfterLaunchChanged(bool value) =>
        Update(s => s.CloseAfterLaunch = value);

    partial void OnAutoStartChanged(bool value)
    {
        if (_initialized && !AutoStartManager.SetEnabled(value))
        {
            AutoStart = AutoStartManager.IsEnabled();
        }
    }

    private void Update(Action<UserSettings> change)
    {
        if (_initialized)
        {
            App.Current.UpdateSettings(change);
        }
    }
}
