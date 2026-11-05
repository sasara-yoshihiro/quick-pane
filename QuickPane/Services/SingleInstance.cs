using Windows.Win32;
using Windows.Win32.Foundation;

namespace QuickPane.Services;

/// <summary>
/// 二重起動の防止。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\QuickPane.SingleInstance.7C1E5A0B";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex, bool isFirstInstance)
    {
        _mutex = mutex;
        IsFirstInstance = isFirstInstance;
    }

    /// <summary>最初に起動したプロセスかどうか</summary>
    public bool IsFirstInstance { get; }

    /// <summary>
    /// 起動中のインスタンスがあるかどうかを確認する。
    /// </summary>
    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        return new SingleInstance(mutex, createdNew);
    }

    /// <summary>
    /// 起動中のインスタンスにランチャーの表示を要求する。
    /// </summary>
    public static void ActivateExistingInstance()
    {
        // 起動直後でメッセージウィンドウがまだ作成されていない場合に備えて少し待つ
        for (var i = 0; i < 20; i++)
        {
            var hwnd = PInvoke.FindWindow(MessageWindow.ClassName, null);
            if (!hwnd.IsNull)
            {
                unsafe
                {
                    uint processId;
                    _ = PInvoke.GetWindowThreadProcessId(hwnd, &processId);
                    PInvoke.AllowSetForegroundWindow(processId);
                }

                PInvoke.PostMessage(hwnd, MessageWindow.ShowLauncherMessage, default, default);
                return;
            }

            Thread.Sleep(100);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
