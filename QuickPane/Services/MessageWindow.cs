using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace QuickPane.Services;

/// <summary>
/// ウィンドウメッセージの受信ハンドラ。
/// </summary>
/// <param name="message">メッセージ</param>
/// <param name="wParam">WPARAM</param>
/// <param name="lParam">LPARAM</param>
/// <returns>処理した場合は true</returns>
internal delegate bool WindowMessageHandler(uint message, WPARAM wParam, LPARAM lParam);

/// <summary>
/// ホットキー・タスクトレイ・二重起動時の表示要求を受信する非表示ウィンドウ。
/// UI スレッドで作成し、WinUI のメッセージループでメッセージを処理する。
/// </summary>
internal sealed unsafe class MessageWindow : IDisposable
{
    /// <summary>ウィンドウクラス名 (二重起動時の検索に使用)</summary>
    public const string ClassName = "QuickPane.MessageWindow";

    /// <summary>ランチャーの表示要求メッセージ</summary>
    public const uint ShowLauncherMessage = PInvoke.WM_APP + 1;

    /// <summary>タスクトレイアイコンのコールバックメッセージ</summary>
    public const uint TrayCallbackMessage = PInvoke.WM_APP + 2;

    private static readonly WNDPROC WindowProcedure = WndProc;

    private static MessageWindow? s_instance;

    private readonly List<WindowMessageHandler> _handlers = [];

    /// <summary>
    /// ウィンドウを作成する。
    /// </summary>
    public MessageWindow()
    {
        s_instance = this;

        var instance = new HINSTANCE(PInvoke.GetModuleHandle(default(PCWSTR)).Value);
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = WindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException($"ウィンドウクラスを登録できませんでした。(エラーコード: {Marshal.GetLastWin32Error()})");
            }

            // タスクバー再作成通知 (TaskbarCreated) を受け取るため、メッセージ専用ではなく非表示のトップレベルウィンドウとする
            Handle = PInvoke.CreateWindowEx(0, new PCWSTR(className), default, 0, 0, 0, 0, 0, HWND.Null, HMENU.Null, instance, null);
        }

        if (Handle.IsNull)
        {
            throw new InvalidOperationException($"ウィンドウを作成できませんでした。(エラーコード: {Marshal.GetLastWin32Error()})");
        }
    }

    /// <summary>ウィンドウハンドル</summary>
    public HWND Handle { get; }

    /// <summary>
    /// メッセージハンドラを追加する。
    /// </summary>
    /// <param name="handler">ハンドラ</param>
    public void AddHandler(WindowMessageHandler handler) => _handlers.Add(handler);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!Handle.IsNull)
        {
            PInvoke.DestroyWindow(Handle);
        }

        s_instance = null;
    }

    private static LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var window = s_instance;
        if (window is not null)
        {
            foreach (var handler in window._handlers)
            {
                try
                {
                    if (handler(message, wParam, lParam))
                    {
                        return new LRESULT(0);
                    }
                }
                catch (Exception ex)
                {
                    App.Log.Error("ウィンドウメッセージの処理中に例外が発生しました。", ex);
                }
            }
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
