using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuickPane.Core.Models;
using Windows.System;
using Windows.UI.Core;

namespace QuickPane.Controls;

/// <summary>
/// ホットキーの入力欄。フォーカス中に押下したキーの組み合わせを取得する (修飾キー必須)。
/// </summary>
public sealed partial class HotkeyBox : UserControl
{
    private static readonly VirtualKey[] ModifierKeys =
    [
        VirtualKey.Control, VirtualKey.LeftControl, VirtualKey.RightControl,
        VirtualKey.Menu, VirtualKey.LeftMenu, VirtualKey.RightMenu,
        VirtualKey.Shift, VirtualKey.LeftShift, VirtualKey.RightShift,
        VirtualKey.LeftWindows, VirtualKey.RightWindows,
    ];

    private HotkeyGesture _gesture = HotkeyGesture.Default;

    private bool _capturing;

    public HotkeyBox()
    {
        InitializeComponent();

        PointerPressed += (_, e) =>
        {
            Focus(FocusState.Pointer);
            StartCapture();
            e.Handled = true;
        };
        GotFocus += (_, _) => StartCapture();
        LostFocus += (_, _) => EndCapture();
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateDisplay();
    }

    /// <summary>キー入力の受付を開始した (ホットキーを一時解除する)</summary>
    public event EventHandler? CaptureStarted;

    /// <summary>キー入力の受付を終了した (ホットキーを再登録する)</summary>
    public event EventHandler? CaptureEnded;

    /// <summary>
    /// 入力されたホットキーを確定する処理。登録できた場合は true を返す。
    /// </summary>
    public Func<HotkeyGesture, bool>? CommitHandler { get; set; }

    /// <summary>現在のホットキー</summary>
    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            UpdateDisplay();
        }
    }

    /// <summary>入力に関する案内 (修飾キーがない場合など) を表示する</summary>
    public event EventHandler<string>? HintChanged;

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static HotkeyModifiers GetModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    /// <summary>
    /// キー入力の受付を終了する (ウィンドウが非アクティブになった場合など)。
    /// </summary>
    public void CancelCapture() => EndCapture();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing)
        {
            // 入力受付外の場合は Enter / Space で受付を開始する
            if (e.Key is VirtualKey.Enter or VirtualKey.Space)
            {
                StartCapture();
                e.Handled = true;
            }

            return;
        }

        var modifiers = GetModifiers();

        // 修飾キーなしの Tab はフォーカス移動、Esc は入力の取り消しとする
        if (e.Key == VirtualKey.Tab && modifiers == HotkeyModifiers.None)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == VirtualKey.Escape && modifiers == HotkeyModifiers.None)
        {
            EndCapture();
            return;
        }

        if (ModifierKeys.Contains(e.Key))
        {
            GestureText.Text = HotkeyGesture.ToDisplayString(modifiers) + " + ...";
            return;
        }

        var virtualKey = (int)e.Key;
        if (modifiers == HotkeyModifiers.None)
        {
            HintChanged?.Invoke(this, "修飾キー (Ctrl / Alt / Shift / Win) と組み合わせて入力してください。");
            UpdateDisplay();
            return;
        }

        if (!HotkeyGesture.IsSupportedKey(virtualKey))
        {
            HintChanged?.Invoke(this, "このキーはホットキーに使用できません。別のキーを入力してください。");
            UpdateDisplay();
            return;
        }

        var gesture = new HotkeyGesture(modifiers, virtualKey);
        if (CommitHandler?.Invoke(gesture) != false)
        {
            // 登録できた場合は入力の受付を終了し、新しいホットキーを有効にする
            _gesture = gesture;
            EndCapture();
        }

        UpdateDisplay();
    }

    private void StartCapture()
    {
        if (_capturing)
        {
            return;
        }

        _capturing = true;
        CaptureStarted?.Invoke(this, EventArgs.Empty);
        UpdateDisplay();
    }

    private void EndCapture()
    {
        if (!_capturing)
        {
            return;
        }

        _capturing = false;
        CaptureEnded?.Invoke(this, EventArgs.Empty);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        GestureText.Text = _gesture.ToDisplayString();
        CaptureRing.Visibility = _capturing ? Visibility.Visible : Visibility.Collapsed;
        EditIcon.Glyph = _capturing ? "" : "";
        ToolTipService.SetToolTip(this, _capturing ? "キーの組み合わせを押してください (Esc で取り消し)" : "クリックしてホットキーを変更");
    }
}
