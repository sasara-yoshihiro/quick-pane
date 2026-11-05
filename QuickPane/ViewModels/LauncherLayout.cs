using CommunityToolkit.Mvvm.ComponentModel;
using QuickPane.Core.Models;

namespace QuickPane.ViewModels;

/// <summary>
/// ランチャーのレイアウト (アイコンサイズ・タイルサイズ・列数、単位は DIP)。
/// </summary>
public sealed partial class LauncherLayout : ObservableObject
{
    /// <summary>アイコンのサイズ</summary>
    [ObservableProperty]
    public partial double IconSize { get; set; } = 40;

    /// <summary>既定アイコン (フォントアイコン) のサイズ</summary>
    [ObservableProperty]
    public partial double GlyphSize { get; set; } = 30;

    /// <summary>タイルの幅</summary>
    [ObservableProperty]
    public partial double TileWidth { get; set; } = 88;

    /// <summary>タイルの高さ</summary>
    [ObservableProperty]
    public partial double TileHeight { get; set; } = 96;

    /// <summary>名前の文字サイズ</summary>
    [ObservableProperty]
    public partial double FontSize { get; set; } = 12;

    /// <summary>1 行あたりのアイテム数</summary>
    [ObservableProperty]
    public partial int Columns { get; set; } = 5;

    /// <summary>
    /// アイコン画像を取得するサイズ (ピクセル)。拡大率 200% までの高 DPI 環境でも粗くならないサイズとする。
    /// </summary>
    public int IconPixelSize => (int)(IconSize * 2);

    /// <summary>
    /// 設定を反映する。
    /// </summary>
    /// <param name="settings">設定</param>
    public void Apply(UserSettings settings)
    {
        (IconSize, TileWidth, TileHeight, FontSize) = settings.IconSize switch
        {
            IconSizeOption.Small => (28d, 76d, 78d, 11d),
            IconSizeOption.Large => (56d, 104d, 116d, 12d),
            _ => (40d, 88d, 96d, 12d),
        };
        GlyphSize = Math.Round(IconSize * 0.75);
        Columns = settings.Columns;
    }
}
