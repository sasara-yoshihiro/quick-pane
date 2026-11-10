using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// 起動する (起動用プロセスからも呼び出す)。
    /// ストアアプリは exe を直接起動せず、AppUserModelID で起動する
    /// (exe の直接起動では管理者として実行できず、アプリの更新で exe のパスも変わるため)。
    /// </summary>
    /// <param name="request">起動内容</param>
    /// <param name="elevate">管理者として実行するかどうか</param>
    /// <returns>起動した対象 (ログ用)</returns>
    internal static string Start(LaunchRequest request, bool elevate)
    {
        var target = request.Path;
        var workingDirectory = request.WorkingDirectory;
        if (PackagedApp.TryGetAppUserModelId(request.Path, out var appUserModelId))
        {
            if (!elevate)
            {
                try
                {
                    AppActivator.Activate(appUserModelId, request.Arguments);
                    return $"{PackagedApp.AppsFolderPrefix}{appUserModelId}";
                }
                catch (COMException ex)
                {
                    // 起動方法の違いで失敗する場合に備え、シェル経由でも起動を試みる
                    Log.Warn($"アプリを起動できなかったため、シェル経由で起動します。AppUserModelID=\"{appUserModelId}\"", ex);
                }
            }

            target = $"{PackagedApp.AppsFolderPrefix}{appUserModelId}";
            workingDirectory = string.Empty;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            Arguments = request.Arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            ErrorDialog = false,
        };
        if (elevate)
        {
            startInfo.Verb = "runas";
        }

        using var process = Process.Start(startInfo);
        return target;
    }

    private static LaunchResult Launch(LauncherItem item, bool elevate)
    {
        var path = Expand(item.Path);
        var request = new LaunchRequest(path, Expand(item.Arguments), ResolveWorkingDirectory(path, Expand(item.WorkingDirectory)));

        // QuickPane を管理者として実行している場合、そのまま起動すると管理者権限を引き継ぐため、
        // 管理者として実行しないアイテムは通常の権限の起動用プロセス経由で起動する
        if (!elevate && Environment.IsPrivilegedProcess)
        {
            var result = UnelevatedLauncher.Launch(request);
            if (result.Success)
            {
                Log.Info($"起動しました (通常の権限)。名前=\"{item.Name}\" パス=\"{path}\" 引数=\"{request.Arguments}\"");
            }
            else
            {
                Log.Error($"起動に失敗しました (通常の権限)。名前=\"{item.Name}\" パス=\"{path}\" {result.ErrorMessage}");
            }

            return result;
        }

        try
        {
            var target = Start(request, elevate);
            Log.Info($"起動しました。名前=\"{item.Name}\" パス=\"{target}\" 引数=\"{request.Arguments}\" 管理者={elevate}");
            return LaunchResult.Succeeded;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == LaunchErrorMessages.ErrorCancelled)
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
        // ストアアプリが更新されている場合・アプリ一覧のパスの場合は、実際の exe のパスを使う
        var path = PackagedApp.ResolveFileSystemPath(Expand(item.Path)).TrimEnd('\\', '/');
        if (PathClassifier.IsUrl(path))
        {
            return LaunchResult.Failed(PackagedApp.IsAppsFolderPath(path)
                ? "このアプリはファイルの場所を取得できません。"
                : "URL のため、場所を開くことはできません。");
        }

        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return LaunchResult.Failed(LaunchErrorMessages.FromWin32(2, false, string.Empty));
            }

            var request = new LaunchRequest("explorer.exe", $"/select,\"{path}\"", string.Empty);
            if (Environment.IsPrivilegedProcess)
            {
                // 管理者権限のエクスプローラが起動しないよう、通常の権限で開く
                var result = UnelevatedLauncher.Launch(request);
                if (!result.Success)
                {
                    return result;
                }
            }
            else
            {
                Start(request, elevate: false);
            }

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
