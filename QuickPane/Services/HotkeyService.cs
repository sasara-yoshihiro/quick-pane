using QuickPane.Core.Models;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace QuickPane.Services;

/// <summary>
/// グローバルホットキーの登録 (RegisterHotKey)。
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 1;

    private const uint ModNoRepeat = 0x4000;

    private readonly MessageWindow _window;

    private bool _registered;

    private bool _suspended;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="window">WM_HOTKEY を受信するウィンドウ</param>
    public HotkeyService(MessageWindow window)
    {
        _window = window;
        _window.AddHandler((message, wParam, _) =>
        {
            if (message != PInvoke.WM_HOTKEY || (int)wParam.Value != HotkeyId)
            {
                return false;
            }

            Pressed?.Invoke(this, EventArgs.Empty);
            return true;
        });
    }

    /// <summary>ホットキーが押下された</summary>
    public event EventHandler? Pressed;

    /// <summary>現在のホットキー</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>ホットキーが登録されているかどうか</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// ホットキーを登録する。登録済みのホットキーは解除する。
    /// </summary>
    /// <param name="gesture">ホットキー</param>
    /// <returns>登録に成功した場合は true</returns>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        Current = gesture;
        if (_suspended)
        {
            return true;
        }

        _registered = PInvoke.RegisterHotKey(_window.Handle, HotkeyId, (HOT_KEY_MODIFIERS)((uint)gesture.Modifiers | ModNoRepeat), (uint)gesture.VirtualKey);
        if (_registered)
        {
            App.Log.Info($"ホットキーを登録しました。{gesture.ToDisplayString()}");
        }
        else
        {
            App.Log.Warn($"ホットキーを登録できませんでした。{gesture.ToDisplayString()}");
        }

        return _registered;
    }

    /// <summary>
    /// 指定したホットキーが登録可能かどうかを確認し、可能であれば現在のホットキーとして登録する。
    /// 登録できない場合は元のホットキーを再登録する。
    /// </summary>
    /// <param name="gesture">ホットキー</param>
    /// <returns>登録に成功した場合は true</returns>
    public bool TryChange(HotkeyGesture gesture)
    {
        var previous = Current;
        var wasSuspended = _suspended;
        _suspended = false;
        var success = Register(gesture);
        if (!success && previous is not null)
        {
            Register(previous.Value);
        }

        if (wasSuspended)
        {
            Suspend();
        }

        return success;
    }

    /// <summary>
    /// 一時的にホットキーを解除する (設定画面でキー入力中など)。
    /// </summary>
    public void Suspend()
    {
        Unregister();
        _suspended = true;
    }

    /// <summary>
    /// 一時解除したホットキーを再登録する。
    /// </summary>
    public void Resume()
    {
        _suspended = false;
        if (Current is not null)
        {
            Register(Current.Value);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Unregister();

    private void Unregister()
    {
        if (_registered)
        {
            PInvoke.UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }
}
