using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuickPane.Core.Shell;
using QuickPane.ViewModels;

namespace QuickPane.Views;

/// <summary>
/// アプリ一覧の項目。
/// </summary>
public sealed partial class InstalledAppItem : ObservableObject
{
    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="app">アプリ</param>
    public InstalledAppItem(InstalledApp app)
    {
        App = app;
    }

    /// <summary>アプリ</summary>
    public InstalledApp App { get; }

    /// <summary>表示名</summary>
    public string Name => App.Name;

    /// <summary>アイコン (取得完了までは null)</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>アイコンの取得を開始したかどうか</summary>
    internal bool IconRequested { get; set; }
}

/// <summary>
/// インストール済みアプリの一覧から、登録するアプリを選択するダイアログ。
/// </summary>
public sealed partial class AppPickerDialog : ContentDialog
{
    private const int IconSize = 48;

    private readonly ObservableCollection<InstalledAppItem> _filtered = [];

    private readonly HashSet<InstalledAppItem> _selected = [];

    private List<InstalledAppItem> _all = [];

    private bool _updatingSelection;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    public AppPickerDialog()
    {
        InitializeComponent();
        AppList.ItemsSource = _filtered;
        Loaded += async (_, _) =>
        {
            SearchBox.Focus(FocusState.Programmatic);
            await LoadAsync();
        };
    }

    /// <summary>選択されたアプリ (一覧の順)</summary>
    public IReadOnlyList<InstalledApp> SelectedApps => _all.Where(_selected.Contains).Select(i => i.App).ToList();

    private async Task LoadAsync()
    {
        try
        {
            var apps = await InstalledApps.GetAsync();
            _all = apps.Select(a => new InstalledAppItem(a)).ToList();
            LoadingPanel.Visibility = Visibility.Collapsed;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            App.Log.Error("アプリ一覧を表示できませんでした。", ex);
            ((ProgressRing)LoadingPanel.Children[0]).IsActive = false;
            LoadingText.Text = "アプリ一覧を取得できませんでした。";
        }
    }

    private void ApplyFilter()
    {
        // 絞り込みで一覧を作り直すと選択が解除されるため、選択状態は別に保持して復元する
        _updatingSelection = true;
        try
        {
            var keyword = SearchBox.Text.Trim();
            _filtered.Clear();
            foreach (var item in _all.Where(i => keyword.Length == 0 || i.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)))
            {
                _filtered.Add(item);
            }

            foreach (var item in _filtered.Where(_selected.Contains))
            {
                AppList.SelectedItems.Add(item);
            }
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection)
        {
            return;
        }

        foreach (var item in e.AddedItems.OfType<InstalledAppItem>())
        {
            _selected.Add(item);
        }

        foreach (var item in e.RemovedItems.OfType<InstalledAppItem>())
        {
            _selected.Remove(item);
        }

        IsPrimaryButtonEnabled = _selected.Count > 0;
        PrimaryButtonText = _selected.Count > 1 ? $"追加 ({_selected.Count} 件)" : "追加";
    }

    private void AppList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // 表示された項目のアイコンだけを取得する
        if (!args.InRecycleQueue && args.Item is InstalledAppItem { IconRequested: false } item)
        {
            item.IconRequested = true;
            _ = LoadIconAsync(item);
        }
    }

    private static async Task LoadIconAsync(InstalledAppItem item) =>
        item.Icon = await ItemViewModel.LoadImageAsync(QuickPane.App.Current.Icons, item.App.Path, string.Empty, IconSize, useCache: false);
}
