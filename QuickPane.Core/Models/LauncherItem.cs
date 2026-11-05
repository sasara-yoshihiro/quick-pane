using System.Text.Json.Serialization;

namespace QuickPane.Core.Models;

/// <summary>
/// ランチャーに登録されたアイテム (アプリ・ファイル・フォルダ・URL)。
/// </summary>
public sealed class LauncherItem
{
    /// <summary>アイテムID</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>表示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ファイル・フォルダのフルパス、または URL (環境変数を含めることができる)</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>引数</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>作業フォルダ (空の場合はパスの親フォルダ)</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>アイコンの場所 ("ファイル,インデックス" 形式、空の場合はパスから自動取得)</summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>管理者として実行するかどうか</summary>
    public bool RunAsAdmin { get; set; }

    /// <summary>種別 (パスから判定)</summary>
    [JsonIgnore]
    public ItemKind Kind => PathClassifier.Classify(Path);

    /// <summary>
    /// 複製を作成する。
    /// </summary>
    public LauncherItem Clone() => (LauncherItem)MemberwiseClone();

    /// <summary>
    /// null の文字列を空文字に正規化する (JSON 読み込み後の補正用)。
    /// </summary>
    internal void Normalize()
    {
        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
        }

        Name ??= string.Empty;
        Path ??= string.Empty;
        Arguments ??= string.Empty;
        WorkingDirectory ??= string.Empty;
        IconPath ??= string.Empty;
    }
}
