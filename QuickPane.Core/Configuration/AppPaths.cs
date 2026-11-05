namespace QuickPane.Core.Configuration;

/// <summary>
/// アプリが使用するフォルダ・ファイルのパス (すべて exe と同じフォルダ配下)。
/// </summary>
public static class AppPaths
{
    /// <summary>exe のあるフォルダ</summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    /// <summary>設定ファイルフォルダ (Config)</summary>
    public static string ConfigDirectory => Path.Combine(BaseDirectory, "Config");

    /// <summary>データフォルダ (Data)</summary>
    public static string DataDirectory => Path.Combine(BaseDirectory, "Data");

    /// <summary>ログフォルダ (Log)</summary>
    public static string LogDirectory => Path.Combine(BaseDirectory, "Log");

    /// <summary>アイコンキャッシュフォルダ (Data/IconCache)</summary>
    public static string IconCacheDirectory => Path.Combine(DataDirectory, "IconCache");

    /// <summary>設定画面で変更した設定 (Data/settings.json)</summary>
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>登録したタブ・アイテム (Data/items.json)</summary>
    public static string ItemsFile => Path.Combine(DataDirectory, "items.json");
}
