namespace QuickPane.Core.Models;

/// <summary>ランチャーの表示位置</summary>
public enum LauncherPosition
{
    /// <summary>マウスカーソル付近</summary>
    Cursor,

    /// <summary>画面中央</summary>
    Center,
}

/// <summary>テーマ</summary>
public enum AppTheme
{
    /// <summary>システム設定に従う</summary>
    System,

    /// <summary>ライト</summary>
    Light,

    /// <summary>ダーク</summary>
    Dark,
}

/// <summary>アイコンサイズ</summary>
public enum IconSizeOption
{
    /// <summary>小</summary>
    Small,

    /// <summary>中</summary>
    Medium,

    /// <summary>大</summary>
    Large,
}

/// <summary>
/// settings.json の内容 (設定画面で変更する設定)。
/// </summary>
public sealed class UserSettings
{
    /// <summary>列数の最小値</summary>
    public const int MinColumns = 3;

    /// <summary>列数の最大値</summary>
    public const int MaxColumns = 10;

    /// <summary>ランチャーを呼び出すホットキー (例: "Ctrl+Space")</summary>
    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    /// <summary>表示位置</summary>
    public LauncherPosition Position { get; set; } = LauncherPosition.Cursor;

    /// <summary>テーマ</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>アイコンサイズ</summary>
    public IconSizeOption IconSize { get; set; } = IconSizeOption.Medium;

    /// <summary>1 行あたりのアイテム数</summary>
    public int Columns { get; set; } = 5;

    /// <summary>アイテム起動後にランチャーを閉じるかどうか</summary>
    public bool CloseAfterLaunch { get; set; } = true;

    /// <summary>
    /// JSON 読み込み後の補正を行う (範囲外の値を既定値・範囲内に戻す)。
    /// </summary>
    public void Normalize()
    {
        if (!HotkeyGesture.TryParse(Hotkey, out _))
        {
            Hotkey = HotkeyGesture.Default.ToString();
        }

        if (!Enum.IsDefined(Position))
        {
            Position = LauncherPosition.Cursor;
        }

        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.System;
        }

        if (!Enum.IsDefined(IconSize))
        {
            IconSize = IconSizeOption.Medium;
        }

        Columns = Math.Clamp(Columns, MinColumns, MaxColumns);
    }
}
