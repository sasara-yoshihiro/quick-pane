using Microsoft.UI.Xaml;

namespace QuickPane.Helpers;

/// <summary>
/// x:Bind の関数バインディング用ヘルパー。
/// </summary>
internal static class Bind
{
    /// <summary>true の場合に表示する。</summary>
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>false の場合に表示する</summary>
    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>1 つ目が true かつ 2 つ目が false の場合に表示する</summary>
    public static Visibility VisibleIfAndNot(bool value, bool exclude) => value && !exclude ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>否定</summary>
    public static bool Not(bool value) => !value;

    /// <summary>null の場合に表示する。</summary>
    public static Visibility VisibleIfNull(object? value) => value is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>文字列が空でないかどうか。</summary>
    public static bool IsNotEmpty(string? value) => !string.IsNullOrEmpty(value);

    /// <summary>ピン留め状態に応じたアイコン。</summary>
    public static string PinGlyph(bool pinned) => pinned ? "" : "";
}
