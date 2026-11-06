using log4net;
using Microsoft.Win32;

namespace QuickPane.Core.Configuration;

/// <summary>
/// Windows サインイン時の自動起動 (HKCU\Software\Microsoft\Windows\CurrentVersion\Run) の管理。
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "QuickPane";

    private static readonly ILog Log = LogManager.GetLogger(typeof(AutoStartManager));

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>
    /// この exe が自動起動に登録されているかどうかを取得する。
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string current && string.Equals(current, Command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 自動起動を登録・解除する。
    /// </summary>
    /// <param name="enabled">登録する場合は true</param>
    /// <returns>成功した場合は true</returns>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else if (key.GetValue(ValueName) is string current && string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            {
                // 別の場所にある QuickPane の登録は削除しない
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            Log.Info($"自動起動を{(enabled ? "登録" : "解除")}しました。");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("自動起動の設定に失敗しました。", ex);
            return false;
        }
    }

    /// <summary>
    /// 自動起動が登録されていて、登録先の exe が存在しない場合 (フォルダを移動した場合)、登録内容を現在の exe のパスに更新する。
    /// 登録先の exe が存在する場合は、別の場所にある QuickPane の登録とみなして変更しない。
    /// </summary>
    public static void RefreshPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is string current
                && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase)
                && !File.Exists(current.Trim().Trim('"')))
            {
                key.SetValue(ValueName, Command);
                Log.Info($"自動起動の登録パスを更新しました。{Command}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("自動起動の登録パスを更新できませんでした。", ex);
        }
    }
}
