namespace QuickPane.Core.Launching;

/// <summary>
/// 起動エラーをユーザ向けのメッセージに変換する。
/// </summary>
public static class LaunchErrorMessages
{
    /// <summary>
    /// Win32 エラーコードからメッセージを作成する。
    /// </summary>
    /// <param name="errorCode">Win32 エラーコード</param>
    /// <param name="elevate">管理者として実行しようとしたかどうか</param>
    /// <param name="systemMessage">システムのエラーメッセージ (該当するメッセージがない場合に使用)</param>
    public static string FromWin32(int errorCode, bool elevate, string systemMessage) => errorCode switch
    {
        2 => "ファイルが見つかりません。パスが正しいか、移動・削除されていないか確認してください。",
        3 => "パスが見つかりません。フォルダが存在するか確認してください。",
        5 => "アクセスが拒否されました。アクセス権を確認するか、管理者として実行してください。",
        15 or 21 => "ドライブの準備ができていません。ドライブが接続されているか確認してください。",
        32 => "ファイルが他のプロセスで使用中のため開けません。",
        53 or 67 or 1203 or 1222 => "ネットワークパスが見つかりません。ネットワークへの接続とパスを確認してください。",
        59 or 64 or 1231 => "ネットワークに接続できません。ネットワークへの接続を確認してください。",
        86 or 1326 => "ネットワークのユーザー名またはパスワードが正しくありません。",
        123 or 161 => "パスの形式が正しくありません。",
        193 or 216 => "有効なアプリケーションではありません。ファイルが破損しているか、この PC では実行できない形式です。",
        267 => "作業フォルダが見つかりません。作業フォルダの設定を確認してください。",
        740 => "管理者権限が必要です。右クリックメニューの「管理者として実行」から起動してください。",
        1155 or 1156 when elevate => "この項目は管理者として実行できません。",
        1155 => "このファイルを開くアプリが関連付けられていません。",
        1260 => "グループ ポリシーにより起動がブロックされています。",
        _ => string.IsNullOrWhiteSpace(systemMessage)
            ? $"起動できませんでした。(エラーコード: {errorCode})"
            : $"{systemMessage} (エラーコード: {errorCode})",
    };

    /// <summary>
    /// 例外からメッセージを作成する。
    /// </summary>
    /// <param name="ex">例外</param>
    public static string FromException(Exception ex) => ex switch
    {
        FileNotFoundException => FromWin32(2, false, ex.Message),
        DirectoryNotFoundException => FromWin32(3, false, ex.Message),
        UnauthorizedAccessException => FromWin32(5, false, ex.Message),
        InvalidOperationException => "パスが指定されていません。",
        _ => ex.Message,
    };
}
