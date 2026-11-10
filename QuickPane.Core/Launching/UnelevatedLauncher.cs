using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace QuickPane.Core.Launching;

/// <summary>
/// 起動内容。
/// </summary>
/// <param name="Path">パス (環境変数は展開済み)</param>
/// <param name="Arguments">引数</param>
/// <param name="WorkingDirectory">作業フォルダ</param>
public sealed record LaunchRequest(string Path, string Arguments, string WorkingDirectory);

/// <summary>
/// QuickPane を管理者として実行している場合に、アイテムを通常の権限で起動する。
/// 管理者権限のプロセスから起動したプロセスは管理者権限を引き継ぐため、通常の権限で動作しているエクスプローラ (デスクトップ) を
/// 親プロセスに指定して QuickPane.exe を起動用プロセスとして起動し (親プロセスの権限を引き継ぐ)、そのプロセスからアイテムを起動する。
/// 起動用プロセスは起動結果 (エラーコード) を終了コードで返す。
/// </summary>
public static class UnelevatedLauncher
{
    /// <summary>起動用プロセスとして起動する場合のコマンドライン引数</summary>
    public const string HelperOption = "--launch-unelevated";

    private const uint ProcessCreateProcess = 0x0080;

    private const uint ExtendedStartupInfoPresent = 0x00080000;

    private const uint CreateSuspended = 0x00000004;

    private const uint CreateUnicodeEnvironment = 0x00000400;

    private const int ProcThreadAttributeParentProcess = 0x00020000;

    private const uint WaitObject0 = 0;

    private const int AsfwAny = -1;

    /// <summary>起動用プロセスの終了を待つ時間</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 起動用プロセスとしてアイテムを起動する (Program.Main から呼び出す)。
    /// </summary>
    /// <param name="payload">起動内容 (JSON を Base64 にしたもの)</param>
    /// <returns>終了コード (0: 成功、正の値: Win32 エラーコード、負の値: HRESULT)</returns>
    public static int RunHelper(string payload)
    {
        // 失敗の詳細 (例外の内容) を残すため、QuickPane 本体と同じログファイルに出力する
        // (log4net.config は MinimalLock のため、本体と同時に書き込める)
        try
        {
            Configuration.AppConfig.ConfigureLogging();
        }
        catch
        {
            // ログを初期化できなくても起動は行う
        }

        var log = log4net.LogManager.GetLogger(typeof(UnelevatedLauncher));
        LaunchRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<LaunchRequest>(Convert.FromBase64String(payload))
                ?? throw new InvalidOperationException("起動内容を読み込めませんでした。");

            // 起動したアプリが前面に表示されるようにする (このプロセスは QuickPane から前面表示の権限を譲り受けている)
            AllowSetForegroundWindow(AsfwAny);
            var target = ItemLauncher.Start(request, elevate: false);
            log.Debug($"起動用プロセスで起動しました。パス=\"{target}\" 引数=\"{request.Arguments}\"");
            return 0;
        }
        catch (Win32Exception ex)
        {
            if (ex.NativeErrorCode != LaunchErrorMessages.ErrorCancelled)
            {
                log.Error($"起動用プロセスで起動に失敗しました。パス=\"{request?.Path}\" 引数=\"{request?.Arguments}\" 作業フォルダ=\"{request?.WorkingDirectory}\" エラーコード={ex.NativeErrorCode}", ex);
            }

            return ex.NativeErrorCode;
        }
        catch (Exception ex)
        {
            log.Error($"起動用プロセスで起動に失敗しました。パス=\"{request?.Path}\" 引数=\"{request?.Arguments}\" 作業フォルダ=\"{request?.WorkingDirectory}\"", ex);
            return ex.HResult;
        }
    }

    /// <summary>
    /// 通常の権限でアイテムを起動する。
    /// </summary>
    /// <param name="request">起動内容</param>
    /// <returns>起動結果</returns>
    internal static LaunchResult Launch(LaunchRequest request)
    {
        var shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero || GetWindowThreadProcessId(shellWindow, out var shellProcessId) == 0)
        {
            return LaunchResult.Failed("エクスプローラ (デスクトップ) が起動していないため、通常の権限で起動できません。エクスプローラを起動してから再度実行してください。");
        }

        var parent = OpenProcess(ProcessCreateProcess, false, shellProcessId);
        if (parent == IntPtr.Zero)
        {
            return Failed(Marshal.GetLastPInvokeError());
        }

        var attributeList = IntPtr.Zero;
        var parentHandle = IntPtr.Zero;
        try
        {
            // 親プロセス (エクスプローラ) を指定する属性リストを作成する
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                Marshal.FreeHGlobal(attributeList);
                attributeList = IntPtr.Zero;
                return Failed(Marshal.GetLastPInvokeError());
            }

            parentHandle = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parentHandle, parent);
            if (!UpdateProcThreadAttribute(attributeList, 0, (IntPtr)ProcThreadAttributeParentProcess, parentHandle, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                return Failed(Marshal.GetLastPInvokeError());
            }

            var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request));
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("QuickPane.exe のパスを取得できません。");
            var commandLine = new StringBuilder($"\"{exe}\" {HelperOption} {payload}");
            var startupInfo = new StartupInfoEx { AttributeList = attributeList };
            startupInfo.StartupInfo.Size = Marshal.SizeOf<StartupInfoEx>();
            if (!CreateProcess(
                exe,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                ExtendedStartupInfoPresent | CreateSuspended | CreateUnicodeEnvironment,
                IntPtr.Zero,
                AppContext.BaseDirectory,
                ref startupInfo,
                out var process))
            {
                return Failed(Marshal.GetLastPInvokeError());
            }

            try
            {
                // 起動したアプリが前面に表示されるよう、前面表示の権限を起動用プロセスに譲る
                AllowSetForegroundWindow((int)process.ProcessId);
                ResumeThread(process.Thread);

                if (WaitForSingleObject(process.Process, (uint)Timeout.TotalMilliseconds) != WaitObject0)
                {
                    // 管理者権限での起動に切り替えると二重起動になるため、エラーとする (起動用プロセスはそのまま起動を続ける)
                    return LaunchResult.Failed("起動の完了を確認できませんでした (応答がありません)。しばらくしてから起動される場合があります。");
                }

                GetExitCodeProcess(process.Process, out var exitCode);
                return (int)exitCode switch
                {
                    0 => LaunchResult.Succeeded,
                    var code => Failed(code),
                };
            }
            finally
            {
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
            }
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (parentHandle != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(parentHandle);
            }

            CloseHandle(parent);
        }
    }

    /// <summary>
    /// 起動用プロセスの終了コード (Win32 エラーコードまたは HRESULT) から起動結果を作成する。
    /// </summary>
    private static LaunchResult Failed(int code) => code switch
    {
        LaunchErrorMessages.ErrorCancelled => LaunchResult.CancelledByUser,
        > 0 => LaunchResult.Failed(LaunchErrorMessages.FromWin32(code, false, new Win32Exception(code).Message)),
        _ => LaunchResult.Failed(LaunchErrorMessages.FromHResult(code)),
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributeList, int attributeCount, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributeList, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previousValue, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll")]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
