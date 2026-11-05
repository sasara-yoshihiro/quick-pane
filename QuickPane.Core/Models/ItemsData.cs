namespace QuickPane.Core.Models;

/// <summary>
/// アイテムをグループ分けするタブ。
/// </summary>
public sealed class LauncherTab
{
    /// <summary>タブID</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>タブ名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>タブ内のアイテム (表示順)</summary>
    public List<LauncherItem> Items { get; set; } = [];
}

/// <summary>
/// items.json の内容 (タブとアイテム)。
/// </summary>
public sealed class ItemsData
{
    /// <summary>既定のタブ名</summary>
    public const string DefaultTabName = "メイン";

    /// <summary>ファイル形式のバージョン</summary>
    public int Version { get; set; } = 1;

    /// <summary>タブ (表示順)</summary>
    public List<LauncherTab> Tabs { get; set; } = [];

    /// <summary>
    /// 初期状態 (「メイン」タブのみ) を作成する。
    /// </summary>
    public static ItemsData CreateDefault() => new()
    {
        Tabs = [new LauncherTab { Name = DefaultTabName }],
    };

    /// <summary>
    /// JSON 読み込み後の補正を行う (null の除去、タブが 0 件の場合は既定タブを追加)。
    /// </summary>
    public void Normalize()
    {
        Tabs = (Tabs ?? []).Where(t => t is not null).ToList();
        foreach (var tab in Tabs)
        {
            if (tab.Id == Guid.Empty)
            {
                tab.Id = Guid.NewGuid();
            }

            tab.Name ??= string.Empty;
            tab.Items = (tab.Items ?? []).Where(i => i is not null).ToList();
            tab.Items.ForEach(i => i.Normalize());
        }

        if (Tabs.Count == 0)
        {
            Tabs.Add(new LauncherTab { Name = DefaultTabName });
        }
    }
}
