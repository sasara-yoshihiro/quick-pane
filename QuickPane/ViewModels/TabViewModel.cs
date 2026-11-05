using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickPane.ViewModels;

/// <summary>
/// ランチャーのタブ。
/// </summary>
public sealed partial class TabViewModel : ObservableObject
{
    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="id">タブID</param>
    /// <param name="name">タブ名</param>
    /// <param name="items">アイテム</param>
    public TabViewModel(Guid id, string name, IEnumerable<ItemViewModel> items)
    {
        Id = id;
        Name = name;
        Items = new ObservableCollection<ItemViewModel>(items);
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>タブID</summary>
    public Guid Id { get; }

    /// <summary>タブ名</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>アイテム (表示順)</summary>
    public ObservableCollection<ItemViewModel> Items { get; }

    /// <summary>アイテムが未登録かどうか</summary>
    public bool IsEmpty => Items.Count == 0;
}
