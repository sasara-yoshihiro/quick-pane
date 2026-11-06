using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace QuickPane.Helpers;

/// <summary>
/// モニタの情報 (座標はすべて物理ピクセル)。
/// </summary>
/// <param name="Cursor">マウスカーソルの位置</param>
/// <param name="WorkArea">作業領域 (タスクバーを除いた領域)</param>
/// <param name="Dpi">DPI</param>
internal readonly record struct MonitorArea(PointInt32 Cursor, RectInt32 WorkArea, uint Dpi)
{
    /// <summary>拡大率 (1.0 = 100%)</summary>
    public double Scale => Dpi / 96.0;
}

/// <summary>
/// WinUI で提供されないウィンドウ操作 (Win32)。
/// </summary>
internal static unsafe class WindowHelper
{
    /// <summary>Windows 11 以降かどうか (角丸などの視覚効果の判定用)</summary>
    public static bool IsWindows11 { get; } = Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// ウィンドウハンドルを取得する。
    /// </summary>
    /// <param name="window">ウィンドウ</param>
    public static HWND GetHandle(Window window) => new(WindowNative.GetWindowHandle(window));

    /// <summary>
    /// タスクバー・Alt + Tab に表示しないツールウィンドウにする。
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    public static void SetToolWindow(HWND hwnd)
    {
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        style &= ~WINDOW_EX_STYLE.WS_EX_APPWINDOW;
        PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)style);
    }

    /// <summary>
    /// ウィンドウの角を丸くする (Windows 11 のみ有効)。
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    public static void SetRoundedCorners(HWND hwnd)
    {
        if (!IsWindows11)
        {
            return;
        }

        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }

    /// <summary>
    /// ウィンドウを前面に表示する。
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    public static void BringToForeground(HWND hwnd) => PInvoke.SetForegroundWindow(hwnd);

    /// <summary>
    /// マウスカーソルがあるモニタの情報を取得する。
    /// </summary>
    public static MonitorArea GetMonitorAtCursor()
    {
        PInvoke.GetCursorPos(out var cursor);
        var monitor = PInvoke.MonitorFromPoint(cursor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return GetMonitorArea(monitor, cursor);
    }

    /// <summary>
    /// 指定した位置に最も近いモニタの情報を取得する (<see cref="MonitorArea.Cursor"/> には指定した位置を設定する)。
    /// </summary>
    /// <param name="point">位置 (物理ピクセル)</param>
    public static MonitorArea GetMonitorAt(PointInt32 point)
    {
        var position = new System.Drawing.Point(point.X, point.Y);
        var monitor = PInvoke.MonitorFromPoint(position, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return GetMonitorArea(monitor, position);
    }

    /// <summary>
    /// マウスカーソルの位置 (物理ピクセル) を取得する。
    /// </summary>
    public static PointInt32 GetCursorPosition()
    {
        PInvoke.GetCursorPos(out var cursor);
        return new PointInt32(cursor.X, cursor.Y);
    }

    /// <summary>
    /// ウィンドウがあるモニタの情報を取得する。
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    public static MonitorArea GetMonitorForWindow(HWND hwnd)
    {
        PInvoke.GetCursorPos(out var cursor);
        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return GetMonitorArea(monitor, cursor);
    }

    /// <summary>
    /// ウィンドウの左上からクライアント領域の左上までのオフセット (物理ピクセル) を取得する。
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    public static (int X, int Y) GetClientOffset(HWND hwnd)
    {
        PInvoke.GetWindowRect(hwnd, out var window);
        var origin = new System.Drawing.Point(0, 0);
        PInvoke.ClientToScreen(hwnd, ref origin);
        return (origin.X - window.left, origin.Y - window.top);
    }

    /// <summary>
    /// ウィンドウ全体とクライアント領域のサイズの差 (枠の大きさ、物理ピクセル) を取得する。
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    public static SizeInt32 GetFrameSize(HWND hwnd)
    {
        PInvoke.GetWindowRect(hwnd, out var window);
        PInvoke.GetClientRect(hwnd, out var client);
        return new SizeInt32(
            (window.right - window.left) - (client.right - client.left),
            (window.bottom - window.top) - (client.bottom - client.top));
    }

    /// <summary>
    /// 矩形が作業領域に収まるよう位置を補正する。
    /// </summary>
    /// <param name="rect">矩形</param>
    /// <param name="workArea">作業領域</param>
    public static RectInt32 FitToWorkArea(RectInt32 rect, RectInt32 workArea)
    {
        var width = Math.Min(rect.Width, workArea.Width);
        var height = Math.Min(rect.Height, workArea.Height);
        var x = Math.Clamp(rect.X, workArea.X, workArea.X + workArea.Width - width);
        var y = Math.Clamp(rect.Y, workArea.Y, workArea.Y + workArea.Height - height);
        return new RectInt32(x, y, width, height);
    }

    private static MonitorArea GetMonitorArea(HMONITOR monitor, System.Drawing.Point cursor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        PInvoke.GetMonitorInfo(monitor, ref info);
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Failed || dpiX == 0)
        {
            dpiX = 96;
        }

        var work = info.rcWork;
        return new MonitorArea(
            new PointInt32(cursor.X, cursor.Y),
            new RectInt32(work.left, work.top, work.right - work.left, work.bottom - work.top),
            dpiX);
    }
}
