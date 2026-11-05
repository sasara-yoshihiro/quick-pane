using CommunityToolkit.Mvvm.ComponentModel;
using QuickPane.Core.Models;
using QuickPane.Core.Shell;

namespace QuickPane.ViewModels;

/// <summary>
/// アイテム編集ダイアログ。
/// </summary>
public sealed partial class ItemEditViewModel : ObservableObject
{
    private readonly LauncherItem _source;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="source">編集するアイテム (新規の場合は空のアイテム)</param>
    /// <param name="isNew">新規登録かどうか</param>
    public ItemEditViewModel(LauncherItem source, bool isNew)
    {
        _source = source;
        IsNew = isNew;
        Name = source.Name;
        Path = source.Path;
        Arguments = source.Arguments;
        WorkingDirectory = source.WorkingDirectory;
        IconPath = source.IconPath;
        RunAsAdmin = source.RunAsAdmin;
    }

    /// <summary>新規登録かどうか</summary>
    public bool IsNew { get; }

    /// <summary>ダイアログのタイトル</summary>
    public string Title => IsNew ? "アイテムを追加" : "アイテムを編集";

    /// <summary>名前</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Name { get; set; }

    /// <summary>パス</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string Path { get; set; }

    /// <summary>引数</summary>
    [ObservableProperty]
    public partial string Arguments { get; set; }

    /// <summary>作業フォルダ</summary>
    [ObservableProperty]
    public partial string WorkingDirectory { get; set; }

    /// <summary>アイコン</summary>
    [ObservableProperty]
    public partial string IconPath { get; set; }

    /// <summary>管理者として実行</summary>
    [ObservableProperty]
    public partial bool RunAsAdmin { get; set; }

    /// <summary>保存できるかどうか (名前・パスが入力済み)</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Path);

    /// <summary>
    /// 参照ボタンで選択したパスを反映する。ショートカットの場合はリンク先等を読み取り、名前が未入力の場合は補完する。
    /// </summary>
    /// <param name="selectedPath">選択したパス</param>
    public async Task ApplySelectedPathAsync(string selectedPath)
    {
        var item = await Task.Run(() => ItemFactory.FromPath(selectedPath));
        Path = item.Path;
        if (!string.IsNullOrEmpty(item.Arguments))
        {
            Arguments = item.Arguments;
        }

        if (!string.IsNullOrEmpty(item.WorkingDirectory))
        {
            WorkingDirectory = item.WorkingDirectory;
        }

        if (!string.IsNullOrEmpty(item.IconPath))
        {
            IconPath = item.IconPath;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = item.Name;
        }
    }

    /// <summary>
    /// 入力内容からアイテムを作成する。
    /// </summary>
    public LauncherItem ToItem()
    {
        var item = _source.Clone();
        item.Name = Name.Trim();
        item.Path = Path.Trim().Trim('"');
        item.Arguments = Arguments.Trim();
        item.WorkingDirectory = WorkingDirectory.Trim().Trim('"');
        item.IconPath = IconPath.Trim();
        item.RunAsAdmin = RunAsAdmin;
        return item;
    }
}
