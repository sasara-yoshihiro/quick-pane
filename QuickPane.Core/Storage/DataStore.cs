using QuickPane.Core.Configuration;
using QuickPane.Core.Models;

namespace QuickPane.Core.Storage;

/// <summary>
/// 設定 (settings.json) と登録アイテム (items.json) の読み書き。
/// </summary>
public static class DataStore
{
    /// <summary>
    /// 設定を読み込む。
    /// </summary>
    public static LoadResult<UserSettings> LoadSettings() =>
        JsonFileStore.Load(AppPaths.SettingsFile, () => new UserSettings(), s => s.Normalize());

    /// <summary>
    /// 登録アイテムを読み込む。
    /// </summary>
    public static LoadResult<ItemsData> LoadItems() =>
        JsonFileStore.Load(AppPaths.ItemsFile, ItemsData.CreateDefault, d => d.Normalize());

    /// <summary>
    /// 設定を保存する。
    /// </summary>
    /// <param name="settings">設定</param>
    public static bool SaveSettings(UserSettings settings) => JsonFileStore.TrySave(AppPaths.SettingsFile, settings);

    /// <summary>
    /// 登録アイテムを保存する。
    /// </summary>
    /// <param name="items">登録アイテム</param>
    public static bool SaveItems(ItemsData items) => JsonFileStore.TrySave(AppPaths.ItemsFile, items);
}
