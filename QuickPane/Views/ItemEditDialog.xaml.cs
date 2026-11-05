using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuickPane.Core.Models;
using QuickPane.ViewModels;
using Windows.Storage.Pickers;
using Windows.Win32.Foundation;
using WinRT.Interop;

namespace QuickPane.Views;

/// <summary>
/// アイテム編集ダイアログ。
/// </summary>
public sealed partial class ItemEditDialog : ContentDialog
{
    private readonly HWND _owner;

    internal ItemEditDialog(ItemEditViewModel viewModel, HWND owner)
    {
        ViewModel = viewModel;
        _owner = owner;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            NameBox.Focus(FocusState.Programmatic);
            _ = UpdatePreviewAsync();
        };
    }

    /// <summary>ViewModel</summary>
    public ItemEditViewModel ViewModel { get; }

    /// <summary>アイコンのプレビュー</summary>
    public IconPreview Preview { get; } = new();

    /// <summary>
    /// パスに対応する既定アイコンを取得する (x:Bind 用)。
    /// </summary>
    /// <param name="path">パス</param>
    public static string GlyphFor(string path) => ItemViewModel.GetDefaultGlyph(PathClassifier.Classify(path));

    private async Task UpdatePreviewAsync()
    {
        var request = ++Preview.Request;
        Preview.Icon = null;
        if (string.IsNullOrWhiteSpace(ViewModel.Path) && string.IsNullOrWhiteSpace(ViewModel.IconPath))
        {
            return;
        }

        var icon = await ItemViewModel.LoadImageAsync(App.Current.Icons, ViewModel.Path, ViewModel.IconPath, 72);
        if (request == Preview.Request)
        {
            Preview.Icon = icon;
        }
    }

    private void Path_LostFocus(object sender, RoutedEventArgs e) => _ = UpdatePreviewAsync();

    private void Icon_LostFocus(object sender, RoutedEventArgs e) => _ = UpdatePreviewAsync();

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _owner);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.ApplySelectedPathAsync(file.Path);
            await UpdatePreviewAsync();
        }
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            await ViewModel.ApplySelectedPathAsync(path);
            await UpdatePreviewAsync();
        }
    }

    private async void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            ViewModel.WorkingDirectory = path;
        }
    }

    private async void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List };
        foreach (var extension in new[] { ".ico", ".exe", ".dll", ".png" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, _owner);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            ViewModel.IconPath = string.Equals(file.FileType, ".png", StringComparison.OrdinalIgnoreCase) || string.Equals(file.FileType, ".ico", StringComparison.OrdinalIgnoreCase)
                ? file.Path
                : $"{file.Path},0";
            await UpdatePreviewAsync();
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _owner);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

}

/// <summary>
/// アイテム編集ダイアログのアイコンプレビュー。
/// </summary>
public sealed partial class IconPreview : ObservableObject
{
    /// <summary>取得要求の番号 (古い取得結果の破棄用)</summary>
    public int Request { get; set; }

    /// <summary>アイコン</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }
}
