using System.Runtime.InteropServices;
using QuickPane.Core.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace QuickPane.Services;

/// <summary>
/// タスクトレイアイコンとメニュー、トースト (バルーン) 通知。
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private const int MenuShowLauncher = 1;

    private const int MenuSettings = 2;

    private const int MenuExit = 3;

    /// <summary>NIN_KEYSELECT (NIN_SELECT | NINF_KEY)</summary>
    private const uint NinKeySelect = PInvoke.NIN_SELECT | 0x1;

    private readonly MessageWindow _window;

    private readonly uint _taskbarCreatedMessage;

    private HICON _icon;

    private bool _added;

    private Action? _balloonClickAction;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="window">コールバックを受信するウィンドウ</param>
    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        _window.AddHandler(OnMessage);
    }

    /// <summary>「ランチャーを表示」が選択された・アイコンが左クリックされた</summary>
    public event EventHandler? ShowLauncherRequested;

    /// <summary>「設定」が選択された</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>「終了」が選択された</summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// タスクトレイにアイコンを追加する。
    /// </summary>
    public void Show()
    {
        if (_icon.IsNull)
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "QuickPane.ico");
            var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForWindow(_window.Handle) is var dpi and > 0 ? dpi : 96);
            fixed (char* path = iconPath)
            {
                _icon = new HICON(PInvoke.LoadImage(HINSTANCE.Null, new PCWSTR(path), GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE).Value);
            }
        }

        var data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = MessageWindow.TrayCallbackMessage;
        data.hIcon = _icon;
        SetText(data.szTip.AsSpan(), App.DisplayName);
        _added = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, data);
        if (!_added)
        {
            App.Log.Warn("タスクトレイにアイコンを追加できませんでした。");
            return;
        }

        data.Anonymous.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, data);
    }

    /// <summary>
    /// トースト (バルーン) 通知を表示する。
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="isError">エラー・警告の場合は true</param>
    /// <param name="clickAction">通知がクリックされたときの処理</param>
    public void Notify(string title, string message, bool isError, Action? clickAction = null)
    {
        if (!_added)
        {
            return;
        }

        _balloonClickAction = clickAction;
        var data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.dwInfoFlags = (isError ? NOTIFY_ICON_INFOTIP_FLAGS.NIIF_WARNING : NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO) | NOTIFY_ICON_INFOTIP_FLAGS.NIIF_LARGE_ICON;
        SetText(data.szInfoTitle.AsSpan(), title);
        SetText(data.szInfo.AsSpan(), message);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, data);
    }

    /// <summary>
    /// メニューのテーマを設定する (ダークモード対応)。
    /// </summary>
    /// <param name="theme">テーマ</param>
    public static void ApplyMenuTheme(AppTheme theme)
    {
        try
        {
            // uxtheme の非公開 API (Windows 10 1903 以降)。失敗しても既定の見た目で動作する
            SetPreferredAppMode(theme switch
            {
                AppTheme.Dark => 2,
                AppTheme.Light => 3,
                _ => 1,
            });
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, data);
            _added = false;
        }

        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _window.Handle,
        uID = IconId,
    };

    private static void SetText(Span<char> buffer, string text)
    {
        buffer.Clear();
        text.AsSpan(0, Math.Min(text.Length, buffer.Length - 1)).CopyTo(buffer);
    }

    private bool OnMessage(uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            // エクスプローラが再起動した場合はアイコンを再登録する
            _added = false;
            Show();
            return false;
        }

        if (message != MessageWindow.TrayCallbackMessage)
        {
            return false;
        }

        // NOTIFYICON_VERSION_4: LOWORD(lParam) がイベント、wParam がアンカー座標
        var notification = (uint)(lParam.Value & 0xFFFF);
        switch (notification)
        {
            case PInvoke.NIN_SELECT:
            case NinKeySelect:
                ShowLauncherRequested?.Invoke(this, EventArgs.Empty);
                break;
            case PInvoke.WM_CONTEXTMENU:
                ShowMenu((short)(wParam.Value & 0xFFFF), (short)((wParam.Value >> 16) & 0xFFFF));
                break;
            case PInvoke.NIN_BALLOONUSERCLICK:
                _balloonClickAction?.Invoke();
                _balloonClickAction = null;
                break;
        }

        return true;
    }

    private void ShowMenu(int x, int y)
    {
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            AppendMenuItem(menu, MenuShowLauncher, "ランチャーを表示");
            AppendMenuItem(menu, MenuSettings, "設定");
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
            AppendMenuItem(menu, MenuExit, "終了");
            PInvoke.SetMenuDefaultItem(menu, MenuShowLauncher, 0);

            // メニュー外をクリックしたときに閉じるよう、前面化してから表示する
            PInvoke.SetForegroundWindow(_window.Handle);
            var command = PInvoke.TrackPopupMenuEx(
                menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
                x,
                y,
                _window.Handle,
                null);
            PInvoke.PostMessage(_window.Handle, PInvoke.WM_NULL, default, default);

            switch ((int)command.Value)
            {
                case MenuShowLauncher:
                    ShowLauncherRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuSettings:
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case MenuExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    private static void AppendMenuItem(HMENU menu, int id, string text)
    {
        fixed (char* p = text)
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)id, new PCWSTR(p));
        }
    }

    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    private static partial int SetPreferredAppMode(int mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    private static partial void FlushMenuThemes();
}
