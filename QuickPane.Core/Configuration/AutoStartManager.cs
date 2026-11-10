using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using log4net;
using Microsoft.Win32;

namespace QuickPane.Core.Configuration;

/// <summary>
/// 自動起動の登録状態。
/// </summary>
/// <param name="Enabled">自動起動 (通常の権限・管理者のいずれか) に登録されているかどうか</param>
/// <param name="Elevated">管理者としての自動起動 (タスク スケジューラ) に登録されているかどうか</param>
public sealed record AutoStartStatus(bool Enabled, bool Elevated);

/// <summary>
/// Windows サインイン時の自動起動の管理。
/// 通常の権限での自動起動は HKCU\Software\Microsoft\Windows\CurrentVersion\Run に、
/// 管理者としての自動起動 (Run キーでは管理者として起動できない) はタスク スケジューラ (最上位の特権で実行) に登録する。
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "QuickPane";

    private const string TaskName = "QuickPane";

    private static readonly TimeSpan SchtasksTimeout = TimeSpan.FromSeconds(15);

    private static readonly ILog Log = LogManager.GetLogger(typeof(AutoStartManager));

    /// <summary>schtasks の出力の文字コード (コンソールの既定の文字コード)</summary>
    private static Encoding ConsoleEncoding
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
    }

    private static string ExePath => Environment.ProcessPath ?? string.Empty;

    private static string Command => $"\"{ExePath}\"";

    /// <summary>
    /// この exe の自動起動の登録状態を取得する (schtasks を実行するため、UI スレッド以外で呼び出す)。
    /// </summary>
    public static AutoStartStatus GetStatus()
    {
        var elevated = IsElevatedEnabled();
        return new AutoStartStatus(elevated || IsRunKeyRegistered(), elevated);
    }

    private static bool IsElevatedEnabled() =>
        GetTaskCommand() is { } command && string.Equals(command, ExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 自動起動を登録・解除する。
    /// </summary>
    /// <param name="enabled">登録する場合は true</param>
    /// <param name="elevated">管理者として起動する場合は true (登録には QuickPane を管理者として実行している必要がある)</param>
    /// <returns>成功した場合は true</returns>
    public static bool SetEnabled(bool enabled, bool elevated)
    {
        try
        {
            if (enabled && elevated)
            {
                if (!RegisterTask())
                {
                    return false;
                }

                RemoveRunKey();
            }
            else
            {
                if (IsElevatedEnabled() && !DeleteTask())
                {
                    return false;
                }

                if (enabled)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                    key.SetValue(ValueName, Command);
                }
                else
                {
                    RemoveRunKey();
                }
            }

            Log.Info($"自動起動を{(enabled ? "登録" : "解除")}しました。管理者={enabled && elevated}");
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

            // タスクの更新には管理者権限が必要なため、管理者として実行している場合のみ更新する
            if (Environment.IsPrivilegedProcess
                && GetTaskCommand() is { } taskCommand
                && !string.Equals(taskCommand, ExePath, StringComparison.OrdinalIgnoreCase)
                && !File.Exists(taskCommand)
                && RegisterTask())
            {
                Log.Info($"管理者としての自動起動の登録パスを更新しました。{ExePath}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("自動起動の登録パスを更新できませんでした。", ex);
        }
    }

    private static bool IsRunKeyRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string current && string.Equals(current, Command, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveRunKey()
    {
        // 別の場所にある QuickPane の登録は削除しない
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is string current && string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// 登録されているタスクの実行ファイルのパスを取得する。
    /// </summary>
    /// <returns>タスクが登録されていない (または取得できない) 場合は null</returns>
    private static string? GetTaskCommand()
    {
        var (exitCode, output) = RunSchtasks($"/Query /TN \"{TaskName}\" /XML");
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            var command = XDocument.Parse(output).Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
            return command?.Trim().Trim('"');
        }
        catch (Exception ex)
        {
            Log.Warn("自動起動のタスクの内容を読み込めませんでした。", ex);
            return null;
        }
    }

    /// <summary>
    /// サインイン時に最上位の特権で QuickPane を起動するタスクを登録する (既存の場合は上書き)。
    /// </summary>
    private static bool RegisterTask()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var command = SecurityElement.Escape(ExePath);
        var directory = SecurityElement.Escape(Path.GetDirectoryName(ExePath) ?? string.Empty);

        // 実行時間の制限・バッテリー駆動時の停止をなくし、優先度を通常にする (既定は 72 時間で停止・優先度が低い)
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Windows サインイン時に QuickPane を管理者として起動します。</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <WorkingDirectory>{directory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var file = Path.Combine(Path.GetTempPath(), $"QuickPane_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            var (exitCode, output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            if (exitCode != 0)
            {
                Log.Error($"自動起動のタスクを登録できませんでした。終了コード={exitCode} {output}");
                return false;
            }

            return true;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static bool DeleteTask()
    {
        var (exitCode, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        if (exitCode != 0)
        {
            Log.Error($"自動起動のタスクを削除できませんでした。終了コード={exitCode} {output}");
            return false;
        }

        return true;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = ConsoleEncoding,
                StandardErrorEncoding = ConsoleEncoding,
            })!;

            // 出力のバッファがいっぱいになって終了しなくならないよう、先に読み取りを開始する
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(SchtasksTimeout))
            {
                process.Kill();
                return (-1, "タイムアウトしました。");
            }

            return (process.ExitCode, output.Result + error.Result);
        }
        catch (Exception ex)
        {
            Log.Warn($"schtasks を実行できませんでした。{arguments}", ex);
            return (-1, ex.Message);
        }
    }
}
