using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickPane.Core.Icons;
using QuickPane.Core.Models;
using Windows.Storage.Streams;

namespace QuickPane.ViewModels;

/// <summary>
/// ランチャーに表示するアイテム。
/// </summary>
public sealed partial class ItemViewModel : ObservableObject
{
    private int _iconRequest;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="model">アイテム</param>
    /// <param name="layout">レイアウト</param>
    public ItemViewModel(LauncherItem model, LauncherLayout layout)
    {
        Model = model;
        Layout = layout;
    }

    /// <summary>アイテム</summary>
    public LauncherItem Model { get; private set; }

    /// <summary>レイアウト</summary>
    public LauncherLayout Layout { get; }

    /// <summary>表示名</summary>
    public string Name => Model.Name;

    /// <summary>ツールチップ</summary>
    public string ToolTip => string.IsNullOrEmpty(Model.Arguments)
        ? $"{Model.Name}\n{Model.Path}"
        : $"{Model.Name}\n{Model.Path} {Model.Arguments}";

    /// <summary>アイコン取得完了までに表示する種別ごとの既定アイコン (Segoe Fluent Icons)</summary>
    public string DefaultGlyph => GetDefaultGlyph(Model.Kind);

    /// <summary>アイコン (取得完了までは null)</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>
    /// 種別ごとの既定アイコンを取得する。
    /// </summary>
    /// <param name="kind">種別</param>
    public static string GetDefaultGlyph(ItemKind kind) => kind switch
    {
        ItemKind.App => "",
        ItemKind.Folder => "",
        ItemKind.Url => "",
        _ => "",
    };

    /// <summary>
    /// アイテムの内容を更新する。
    /// </summary>
    /// <param name="model">更新後のアイテム</param>
    public void Update(LauncherItem model)
    {
        Model = model;
        _iconRequest++;
        Icon = null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(DefaultGlyph));
    }

    /// <summary>
    /// アイコンをバックグラウンドで取得して表示する。取得できない場合は既定アイコンのままとする。
    /// </summary>
    /// <param name="service">アイコン取得</param>
    public async Task LoadIconAsync(IconService service)
    {
        var request = ++_iconRequest;
        var icon = await LoadImageAsync(service, Model.Path, Model.IconPath, Layout.IconPixelSize);

        // 取得中にアイテムが更新された場合は古い結果を破棄する
        if (request == _iconRequest)
        {
            Icon = icon;
        }
    }

    /// <summary>
    /// アイコン画像を読み込む。
    /// </summary>
    /// <param name="service">アイコン取得</param>
    /// <param name="path">アイテムのパス</param>
    /// <param name="iconPath">アイコンの場所</param>
    /// <param name="size">サイズ (ピクセル)</param>
    /// <returns>読み込めなかった場合は null</returns>
    public static async Task<ImageSource?> LoadImageAsync(IconService service, string path, string iconPath, int size)
    {
        var file = await service.GetIconFileAsync(path, iconPath, size);
        if (file is null)
        {
            return null;
        }

        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(file)).WaitAsync(TimeSpan.FromSeconds(5));
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage { DecodePixelWidth = size, DecodePixelType = DecodePixelType.Physical };
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex)
        {
            App.Log.Warn($"アイコン画像を読み込めませんでした。{file}", ex);
            return null;
        }
    }
}
