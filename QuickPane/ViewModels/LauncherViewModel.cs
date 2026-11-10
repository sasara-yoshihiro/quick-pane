using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickPane.Core.Icons;
using QuickPane.Core.Launching;
using QuickPane.Core.Models;
using QuickPane.Core.Shell;
using QuickPane.Core.Storage;

namespace QuickPane.ViewModels;

/// <summary>
/// ランチャー画面。タブ・アイテムの管理と起動を行う。
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    private readonly IconService _iconService;

    /// <summary>起動処理中のアイテム (同じアイテムの二重起動を防ぐ)</summary>
    private readonly HashSet<ItemViewModel> _launching = [];

    /// <summary>起動・場所を開く処理の実行中の件数</summary>
    private int _busyCount;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="data">登録アイテム</param>
    /// <param name="iconService">アイコン取得</param>
    /// <param name="layout">レイアウト</param>
    internal LauncherViewModel(ItemsData data, IconService iconService, LauncherLayout layout)
    {
        _iconService = iconService;
        Layout = layout;
        Tabs = new ObservableCollection<TabViewModel>(
            data.Tabs.Select(t => new TabViewModel(t.Id, t.Name, t.Items.Select(CreateItem))));
        SelectedTabIndex = 0;
        ReloadIcons();
    }

    /// <summary>タブ・アイテムの件数が変化した (ウィンドウサイズの再計算用)</summary>
    public event EventHandler? ItemsChanged;

    /// <summary>レイアウト</summary>
    public LauncherLayout Layout { get; }

    /// <summary>タブ (表示順)</summary>
    public ObservableCollection<TabViewModel> Tabs { get; }

    /// <summary>選択中のタブのインデックス</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTab), nameof(DisplayedItems))]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>検索欄を表示しているかどうか</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive), nameof(DisplayedItems), nameof(IsSearchNoResult))]
    public partial bool IsSearchOpen { get; set; }

    /// <summary>検索文字列</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive), nameof(DisplayedItems), nameof(IsSearchNoResult))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>検索中 (検索欄に文字を入力している) かどうか。検索中は全タブの検索結果を表示する</summary>
    public bool IsSearchActive => IsSearchOpen && !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>検索結果 (全タブのアイテムのうち、名前が検索文字列を含むもの)</summary>
    public ObservableCollection<ItemViewModel> SearchResults { get; } = [];

    /// <summary>検索結果が 0 件かどうか</summary>
    public bool IsSearchNoResult => IsSearchActive && SearchResults.Count == 0;

    /// <summary>表示するアイテム (検索中は検索結果、それ以外は選択中のタブのアイテム)</summary>
    public ObservableCollection<ItemViewModel>? DisplayedItems => IsSearchActive ? SearchResults : SelectedTab?.Items;

    /// <summary>選択中のタブ</summary>
    public TabViewModel? SelectedTab => SelectedTabIndex >= 0 && SelectedTabIndex < Tabs.Count ? Tabs[SelectedTabIndex] : null;

    /// <summary>フォーカスが外れても閉じない (ピン留め) かどうか</summary>
    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    /// <summary>起動処理中かどうか</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>エラー通知を表示するかどうか</summary>
    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    /// <summary>エラー通知のタイトル</summary>
    [ObservableProperty]
    public partial string ErrorTitle { get; set; } = string.Empty;

    /// <summary>エラー通知のメッセージ</summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>アイテム数が最も多いタブのアイテム数</summary>
    public int MaxItemCount => Tabs.Count == 0 ? 0 : Tabs.Max(t => t.Items.Count);

    /// <summary>
    /// 検索を終了し、選択中のタブの表示に戻す。
    /// </summary>
    public void CloseSearch()
    {
        SearchText = string.Empty;
        IsSearchOpen = false;
    }

    partial void OnSearchTextChanged(string value) => UpdateSearchResults();

    /// <summary>
    /// 検索結果を更新する。大文字・小文字、全角・半角、ひらがな・カタカナを区別せずに名前で検索する。
    /// </summary>
    private void UpdateSearchResults()
    {
        var keyword = SearchText.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;
        var matches = keyword.Length == 0
            ? new List<ItemViewModel>()
            : Tabs.SelectMany(t => t.Items).Where(i => compare.IndexOf(i.Name, keyword, options) >= 0).ToList();

        if (!matches.SequenceEqual(SearchResults))
        {
            SearchResults.Clear();
            foreach (var item in matches)
            {
                SearchResults.Add(item);
            }
        }

        OnPropertyChanged(nameof(IsSearchNoResult));
    }

    /// <summary>
    /// タブを切り替える。
    /// </summary>
    /// <param name="delta">移動量 (1: 次のタブ、-1: 前のタブ)</param>
    public void SelectNextTab(int delta)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        SelectedTabIndex = ((SelectedTabIndex + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
    }

    /// <summary>
    /// タブを追加して選択する。
    /// </summary>
    /// <param name="name">タブ名</param>
    public void AddTab(string name)
    {
        Tabs.Add(new TabViewModel(Guid.NewGuid(), name.Trim(), []));
        SelectedTabIndex = Tabs.Count - 1;
        Save();
    }

    /// <summary>
    /// タブ名を変更する。
    /// </summary>
    /// <param name="tab">タブ</param>
    /// <param name="name">新しいタブ名</param>
    public void RenameTab(TabViewModel tab, string name)
    {
        tab.Name = name.Trim();
        Save();
    }

    /// <summary>
    /// タブを削除する (タブ内のアイテムも削除される)。最後の 1 つのタブは削除できない。
    /// </summary>
    /// <param name="tab">タブ</param>
    public void DeleteTab(TabViewModel tab)
    {
        if (Tabs.Count <= 1)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        var selected = SelectedTab;
        Tabs.Remove(tab);
        SelectedTabIndex = selected == tab ? Math.Min(index, Tabs.Count - 1) : Tabs.IndexOf(selected!);
        App.Log.Info($"タブを削除しました。タブ名=\"{tab.Name}\" アイテム数={tab.Items.Count}");
        Save();
    }

    /// <summary>
    /// ドラッグによるタブの並べ替え後に、選択状態を戻して保存する。
    /// </summary>
    /// <param name="selected">並べ替え前に選択していたタブ</param>
    public void CompleteTabReorder(TabViewModel? selected)
    {
        if (selected is not null)
        {
            SelectedTabIndex = Tabs.IndexOf(selected);
        }

        Save();
    }

    /// <summary>
    /// ドロップされたファイル・フォルダをアイテムとして登録する。
    /// </summary>
    /// <param name="paths">パス</param>
    /// <param name="tab">登録先のタブ</param>
    public async Task AddPathsAsync(IReadOnlyList<string> paths, TabViewModel tab)
    {
        // ショートカットの読み取り・フォルダの判定はネットワーク上のパスで時間がかかる場合があるため、バックグラウンドで行う
        var items = await Task.Run(() => paths.Select(ItemFactory.FromPath).ToList());
        foreach (var item in items)
        {
            AddItem(item, tab, save: false);
            App.Log.Info($"アイテムを登録しました。名前=\"{item.Name}\" パス=\"{item.Path}\"");
        }

        Save();
    }

    /// <summary>
    /// アイテムを追加する。
    /// </summary>
    /// <param name="item">アイテム</param>
    /// <param name="tab">追加先のタブ</param>
    /// <param name="save">保存するかどうか</param>
    public ItemViewModel AddItem(LauncherItem item, TabViewModel tab, bool save = true)
    {
        var viewModel = CreateItem(item);
        tab.Items.Add(viewModel);
        _ = viewModel.LoadIconAsync(_iconService);
        if (save)
        {
            Save();
        }

        return viewModel;
    }

    /// <summary>
    /// アイテムを更新する。
    /// </summary>
    /// <param name="item">アイテム</param>
    /// <param name="edited">更新後の内容</param>
    public void UpdateItem(ItemViewModel item, LauncherItem edited)
    {
        item.Update(edited);
        _ = item.LoadIconAsync(_iconService);
        Save();
    }

    /// <summary>
    /// アイテムを削除する。
    /// </summary>
    /// <param name="item">アイテム</param>
    public void DeleteItem(ItemViewModel item)
    {
        var tab = FindTab(item);
        if (tab?.Items.Remove(item) == true)
        {
            App.Log.Info($"アイテムを削除しました。名前=\"{item.Name}\"");
            Save();
        }
    }

    /// <summary>
    /// アイテムを別のタブへ移動する (移動先の末尾に追加)。
    /// </summary>
    /// <param name="item">アイテム</param>
    /// <param name="target">移動先のタブ</param>
    public void MoveItem(ItemViewModel item, TabViewModel target)
    {
        var source = FindTab(item);
        if (source is null || source == target)
        {
            return;
        }

        source.Items.Remove(item);
        target.Items.Add(item);
        Save();
    }

    /// <summary>
    /// アイテムが属するタブを取得する。
    /// </summary>
    /// <param name="item">アイテム</param>
    public TabViewModel? FindTab(ItemViewModel item) => Tabs.FirstOrDefault(t => t.Items.Contains(item));

    /// <summary>
    /// アイテムを起動する。
    /// </summary>
    /// <param name="item">アイテム</param>
    /// <param name="runAsAdmin">管理者として実行するかどうか (null の場合はアイテムの設定に従う)</param>
    /// <returns>起動結果 (同じアイテムを起動中の場合は何もせず、キャンセル扱いの結果を返す)</returns>
    public async Task<LaunchResult> LaunchAsync(ItemViewModel item, bool? runAsAdmin = null)
    {
        // Enter の連打等で同じアイテムが二重に起動されないよう、起動中のアイテムは起動しない
        if (!_launching.Add(item))
        {
            return LaunchResult.CancelledByUser;
        }

        IsErrorOpen = false;
        BeginBusy();
        try
        {
            return await ItemLauncher.LaunchAsync(item.Model, runAsAdmin);
        }
        finally
        {
            _launching.Remove(item);
            EndBusy();
        }
    }

    /// <summary>
    /// アイテムの場所をエクスプローラで開く。
    /// </summary>
    /// <param name="item">アイテム</param>
    public async Task<LaunchResult> OpenLocationAsync(ItemViewModel item)
    {
        IsErrorOpen = false;
        BeginBusy();
        try
        {
            return await ItemLauncher.OpenLocationAsync(item.Model);
        }
        finally
        {
            EndBusy();
        }
    }

    // 複数の起動を並行して行う場合も、すべて終わるまで処理中の表示を続ける
    private void BeginBusy() => IsBusy = ++_busyCount > 0;

    private void EndBusy() => IsBusy = --_busyCount > 0;

    /// <summary>
    /// エラー通知を表示する。
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    public void ShowError(string title, string message)
    {
        ErrorTitle = title;
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    /// <summary>
    /// 登録アイテムで使用していないアイコンキャッシュを削除する。
    /// </summary>
    public void PruneIconCache()
    {
        var items = Tabs.SelectMany(t => t.Items).Select(i => (i.Model.Path, i.Model.IconPath)).ToList();
        var size = Layout.IconPixelSize;
        _ = Task.Run(() => _iconService.PruneCache(items, size));
    }

    /// <summary>
    /// すべてのアイテムのアイコンを再取得する (アイコンサイズ変更時など)。
    /// </summary>
    public void ReloadIcons()
    {
        foreach (var item in Tabs.SelectMany(t => t.Items))
        {
            _ = item.LoadIconAsync(_iconService);
        }
    }

    /// <summary>
    /// 登録アイテムを保存する。
    /// </summary>
    public void Save()
    {
        var data = new ItemsData
        {
            Tabs = Tabs.Select(t => new LauncherTab
            {
                Id = t.Id,
                Name = t.Name,
                Items = t.Items.Select(i => i.Model).ToList(),
            }).ToList(),
        };
        DataStore.SaveItems(data);
        if (IsSearchActive)
        {
            // 検索中にアイテムを編集・削除した場合は検索結果に反映する
            UpdateSearchResults();
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private ItemViewModel CreateItem(LauncherItem item) => new(item, Layout);
}
