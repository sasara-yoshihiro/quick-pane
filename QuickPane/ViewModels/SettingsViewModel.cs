using CommunityToolkit.Mvvm.ComponentModel;
using QuickPane.Core.Configuration;
using QuickPane.Core.Models;

namespace QuickPane.ViewModels;

/// <summary>
/// 設定画面。変更は即時反映・保存する。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private bool _initialized;

    /// <summary>自動起動の状態をプログラムから設定中 (変更処理を行わない)</summary>
    private bool _settingAutoStart;

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
        HotkeyError = App.Current.Hotkey.IsRegistered ? string.Empty : "このホットキーは他のアプリで使用されているため登録できませんでした。別の組み合わせを設定してください。";
        _initialized = true;

        // 自動起動の状態の取得 (schtasks の実行) は時間がかかる場合があるため、バックグラウンドで行う
        _ = RunAutoStartTaskAsync(() => true);
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
    [NotifyPropertyChangedFor(nameof(CanChangeAutoStartElevated))]
    public partial bool AutoStart { get; set; }

    /// <summary>自動起動の状態の取得・変更中かどうか (変更中はスイッチを無効にする)</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeAutoStart), nameof(CanChangeAutoStartElevated))]
    public partial bool IsAutoStartBusy { get; set; } = true;

    /// <summary>管理者として自動起動</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeAutoStart))]
    public partial bool AutoStartElevated { get; set; }

    /// <summary>
    /// 自動起動を変更できるかどうか。
    /// 管理者としての自動起動 (タスク スケジューラ) の解除には管理者権限が必要なため、管理者として実行していない場合は変更できない。
    /// </summary>
    public bool CanChangeAutoStart => !IsAutoStartBusy && (App.IsElevated || !AutoStartElevated);

    /// <summary>管理者として自動起動を変更できるかどうか (登録・解除には管理者権限が必要)</summary>
    public bool CanChangeAutoStartElevated => !IsAutoStartBusy && App.IsElevated && AutoStart;

    /// <summary>管理者として自動起動の説明</summary>
    public string AutoStartElevatedDescription => App.IsElevated
        ? "サインイン時に QuickPane を管理者として起動します (タスク スケジューラに登録します)。"
        : "サインイン時に QuickPane を管理者として起動します。変更するには、QuickPane を管理者として実行してください。";

    /// <summary>設定画面のタイトル</summary>
    public string WindowTitle => App.IsElevated ? "QuickPane 設定 (管理者として実行中)" : "QuickPane 設定";

    /// <summary>列数の最小値</summary>
    public double MinColumns => UserSettings.MinColumns;

    /// <summary>列数の最大値</summary>
    public double MaxColumns => UserSettings.MaxColumns;

    /// <summary>バージョン</summary>
    public string Version => App.IsElevated ? $"バージョン {App.Version} (管理者として実行中)" : $"バージョン {App.Version}";

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

    // アイコンサイズ・列数を変更した場合は、ランチャーのサイズを自動に戻して変更後のレイアウトに合わせる
    partial void OnIconSizeIndexChanged(int value) =>
        Update(s =>
        {
            s.IconSize = (IconSizeOption)value;
            s.ResetLauncherSize();
        });

    partial void OnColumnsChanged(double value)
    {
        if (!double.IsNaN(value))
        {
            Update(s =>
            {
                s.Columns = (int)Math.Round(value);
                s.ResetLauncherSize();
            });
        }
    }

    partial void OnCloseAfterLaunchChanged(bool value) =>
        Update(s => s.CloseAfterLaunch = value);

    partial void OnAutoStartChanged(bool value)
    {
        if (!_settingAutoStart)
        {
            var elevated = value && AutoStartElevated;
            _ = RunAutoStartTaskAsync(() => AutoStartManager.SetEnabled(value, elevated));
        }
    }

    partial void OnAutoStartElevatedChanged(bool value)
    {
        if (!_settingAutoStart && AutoStart)
        {
            _ = RunAutoStartTaskAsync(() => AutoStartManager.SetEnabled(true, value));
        }
    }

    /// <summary>
    /// 自動起動の登録・解除をバックグラウンドで行い、登録状態を画面に反映する。
    /// </summary>
    /// <param name="change">登録・解除処理</param>
    private async Task RunAutoStartTaskAsync(Func<bool> change)
    {
        IsAutoStartBusy = true;
        try
        {
            // 変更後の登録状態を照会して画面に反映する (照会は 1 回にまとめる)
            var status = await Task.Run(() =>
            {
                change();
                return AutoStartManager.GetStatus();
            });

            _settingAutoStart = true;
            AutoStart = status.Enabled;
            AutoStartElevated = status.Elevated;
        }
        catch (Exception ex)
        {
            App.Log.Error("自動起動の状態を取得できませんでした。", ex);
        }
        finally
        {
            _settingAutoStart = false;
            IsAutoStartBusy = false;
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
