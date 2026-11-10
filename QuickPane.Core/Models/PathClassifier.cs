using System.Text.RegularExpressions;

namespace QuickPane.Core.Models;

/// <summary>アイテムの種別</summary>
public enum ItemKind
{
    /// <summary>アプリ (exe・ショートカット等)</summary>
    App,

    /// <summary>ファイル</summary>
    File,

    /// <summary>フォルダ</summary>
    Folder,

    /// <summary>URL</summary>
    Url,
}

/// <summary>
/// パス文字列からアイテム種別を判定する。
/// ネットワーク上のパスでも UI をブロックしないよう、ファイルシステムにはアクセスせず文字列のみで判定する。
/// </summary>
public static partial class PathClassifier
{
    private static readonly HashSet<string> AppExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".lnk", ".msc", ".ps1", ".vbs", ".appref-ms",
    };

    /// <summary>
    /// 種別を判定する。
    /// </summary>
    /// <param name="path">パスまたは URL</param>
    public static ItemKind Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ItemKind.File;
        }

        var trimmed = path.Trim().TrimEnd('\\', '/');
        if (IsUrl(trimmed))
        {
            // アプリ一覧のアプリ (shell:AppsFolder\...) はアプリ、shell:startup 等はフォルダとして扱う
            if (trimmed.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
            {
                return ItemKind.App;
            }

            return trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? ItemKind.Folder : ItemKind.Url;
        }

        if (path.TrimEnd().EndsWith('\\') || IsDriveRoot(trimmed))
        {
            return ItemKind.Folder;
        }

        var extension = System.IO.Path.GetExtension(trimmed);
        if (string.IsNullOrEmpty(extension))
        {
            return ItemKind.Folder;
        }

        return AppExtensions.Contains(extension) ? ItemKind.App : ItemKind.File;
    }

    /// <summary>
    /// URL (スキーム付き文字列) かどうかを判定する。ドライブ文字 (C:) は URL とみなさない。
    /// </summary>
    /// <param name="path">パスまたは URL</param>
    public static bool IsUrl(string? path) => !string.IsNullOrWhiteSpace(path) && UrlSchemeRegex().IsMatch(path.Trim());

    /// <summary>
    /// URL のスキーム (例: "https") を取得する。
    /// </summary>
    /// <param name="url">URL</param>
    public static string? GetScheme(string url)
    {
        var match = UrlSchemeRegex().Match(url.Trim());
        return match.Success ? match.Groups["scheme"].Value : null;
    }

    private static bool IsDriveRoot(string path) => path.Length == 2 && path[1] == ':' && char.IsLetter(path[0]);

    [GeneratedRegex(@"^(?<scheme>[A-Za-z][A-Za-z0-9+.\-]+):(?![\\])")]
    private static partial Regex UrlSchemeRegex();
}
