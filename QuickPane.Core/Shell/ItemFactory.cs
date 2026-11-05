using QuickPane.Core.Models;

namespace QuickPane.Core.Shell;

/// <summary>
/// ドロップされたファイル・フォルダからアイテムを作成する。
/// </summary>
public static class ItemFactory
{
    /// <summary>
    /// パスからアイテムを作成する。ショートカット (.lnk) の場合はリンク先・引数・作業フォルダを読み取る。
    /// ファイルシステムにアクセスするため、バックグラウンドで呼び出すこと。
    /// </summary>
    /// <param name="path">ファイル・フォルダのパス</param>
    public static LauncherItem FromPath(string path)
    {
        var item = new LauncherItem
        {
            Name = GetDisplayName(path, Directory.Exists(path)),
            Path = path,
        };

        if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = ShortcutReader.Read(path);

            // リンク先がファイルシステム上にないショートカット (コントロールパネル等) は .lnk のまま登録する
            if (shortcut is not null && !string.IsNullOrWhiteSpace(shortcut.TargetPath))
            {
                item.Path = shortcut.TargetPath;
                item.Arguments = shortcut.Arguments;
                item.WorkingDirectory = shortcut.WorkingDirectory;
                item.IconPath = shortcut.IconLocation;
            }
        }

        return item;
    }

    /// <summary>
    /// パスから表示名を作成する (拡張子を除いたファイル名、ドライブの場合は "C:")。
    /// </summary>
    /// <param name="path">パス</param>
    /// <param name="isDirectory">フォルダかどうか (null の場合はパス文字列から判定)</param>
    public static string GetDisplayName(string path, bool? isDirectory = null)
    {
        if (PathClassifier.IsUrl(path))
        {
            return Uri.TryCreate(path, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : path;
        }

        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length <= 2)
        {
            return trimmed;
        }

        var folder = isDirectory ?? PathClassifier.Classify(path) == ItemKind.Folder;
        var name = folder ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
