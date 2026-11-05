using System.ComponentModel;
using System.Diagnostics;
using log4net;
using QuickPane.Core.Models;

namespace QuickPane.Core.Launching;

/// <summary>
/// アイテム起動結果。
/// </summary>
/// <param name="Success">起動に成功したかどうか</param>
/// <param name="Cancelled">UAC の確認ダイアログでキャンセルされたかどうか</param>
/// <param name="ErrorMessage">失敗した場合のエラーメッセージ</param>
public sealed record LaunchResult(bool Success, bool Cancelled = false, string? ErrorMessage = null)
{
    /// <summary>成功</summary>
    public static LaunchResult Succeeded { get; } = new(true);

    /// <summary>UAC でキャンセルされた</summary>
    public static LaunchResult CancelledByUser { get; } = new(false, Cancelled: true);

    /// <summary>
    /// 失敗。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public static LaunchResult Failed(string message) => new(false, ErrorMessage: message);
}

/// <summary>
/// アイテムの起動 (シェル経由)。起動に時間がかかる場合でも呼び出し元をブロックしないよう、バックグラウンドで実行する。
/// </summary>
public static class ItemLauncher
{
    private const int ErrorCancelled = 1223;

    private static readonly ILog Log = LogManager.GetLogger(typeof(ItemLauncher));

    /// <summary>
    /// アイテムを起動する。
    /// </summary>
    /// <param name="item">アイテム</param>
    /// <param name="runAsAdmin">管理者として実行するかどうか (null の場合はアイテムの設定に従う)</param>
    public static Task<LaunchResult> LaunchAsync(LauncherItem item, bool? runAsAdmin = null)
    {
        var snapshot = item.Clone();
        var elevate = runAsAdmin ?? snapshot.RunAsAdmin;
        return Task.Run(() => Launch(snapshot, elevate));
    }

    /// <summary>
    /// アイテムの場所をエクスプローラで開く (アイテムを選択した状態)。
    /// </summary>
    /// <param name="item">アイテム</param>
    public static Task<LaunchResult> OpenLocationAsync(LauncherItem item)
    {
        var snapshot = item.Clone();
        return Task.Run(() => OpenLocation(snapshot));
    }

    private static LaunchResult Launch(LauncherItem item, bool elevate)
    {
        var path = Expand(item.Path);
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = Expand(item.Arguments),
            WorkingDirectory = ResolveWorkingDirectory(path, Expand(item.WorkingDirectory)),
            UseShellExecute = true,
            ErrorDialog = false,
        };
        if (elevate)
        {
            startInfo.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(startInfo);
            Log.Info($"起動しました。名前=\"{item.Name}\" パス=\"{path}\" 引数=\"{startInfo.Arguments}\" 管理者={elevate}");
            return LaunchResult.Succeeded;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            Log.Info($"起動がキャンセルされました。名前=\"{item.Name}\" パス=\"{path}\"");
            return LaunchResult.CancelledByUser;
        }
        catch (Win32Exception ex)
        {
            Log.Error($"起動に失敗しました。名前=\"{item.Name}\" パス=\"{path}\" エラーコード={ex.NativeErrorCode}", ex);
            return LaunchResult.Failed(LaunchErrorMessages.FromWin32(ex.NativeErrorCode, elevate, ex.Message));
        }
        catch (Exception ex)
        {
            Log.Error($"起動に失敗しました。名前=\"{item.Name}\" パス=\"{path}\"", ex);
            return LaunchResult.Failed(LaunchErrorMessages.FromException(ex));
        }
    }

    private static LaunchResult OpenLocation(LauncherItem item)
    {
        var path = Expand(item.Path).TrimEnd('\\', '/');
        if (PathClassifier.IsUrl(path))
        {
            return LaunchResult.Failed("URL のため、場所を開くことはできません。");
        }

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return LaunchResult.Failed(LaunchErrorMessages.FromWin32(2, false, string.Empty));
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
            Log.Info($"ファイルの場所を開きました。パス=\"{path}\"");
            return LaunchResult.Succeeded;
        }
        catch (Exception ex)
        {
            Log.Error($"ファイルの場所を開けませんでした。パス=\"{path}\"", ex);
            return LaunchResult.Failed(LaunchErrorMessages.FromException(ex));
        }
    }

    private static string ResolveWorkingDirectory(string path, string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            return workingDirectory;
        }

        // 未指定の場合はパスの親フォルダ (URL・ルートフォルダの場合は指定しない)
        if (PathClassifier.IsUrl(path) || !Path.IsPathFullyQualified(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string Expand(string value) => Environment.ExpandEnvironmentVariables(value ?? string.Empty).Trim();
}
