using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickPane.Core.Configuration;
using QuickPane.Helpers;
using QuickPane.ViewModels;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace QuickPane.Views;

/// <summary>
/// 設定画面 (左ナビゲーション + カード型の設定項目)。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double DefaultWidth = 920;

    private const double DefaultHeight = 660;

    private static readonly nuint SubclassId = 1;

    private static SUBCLASSPROC? s_subclassProc;

    private readonly HWND _hwnd;

    private bool _capturingHotkey;

    internal SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        _hwnd = WindowHelper.GetHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "QuickPane.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
        }

        HotkeyInput.Gesture = ViewModel.Hotkey;
        HotkeyInput.CommitHandler = ViewModel.TryChangeHotkey;
        HotkeyInput.HintChanged += (_, hint) => ViewModel.HotkeyError = hint;
        HotkeyInput.CaptureStarted += (_, _) =>
        {
            // 入力中は現在のホットキーが反応しないよう一時解除する
            _capturingHotkey = true;
            App.Current.Hotkey.Suspend();
        };
        HotkeyInput.CaptureEnded += (_, _) =>
        {
            _capturingHotkey = false;
            App.Current.Hotkey.Resume();
        };

        // Alt + Space 等でシステムメニューが開かないよう、ホットキー入力中は SC_KEYMENU を無視する
        s_subclassProc ??= SubclassProc;
        PInvoke.SetWindowSubclass(_hwnd, s_subclassProc, SubclassId, 0);

        Navigation.SelectedItem = Navigation.MenuItems[0];
        App.Current.SettingsChanged += OnSettingsChanged;
        Closed += (_, _) =>
        {
            App.Current.SettingsChanged -= OnSettingsChanged;
            PInvoke.RemoveWindowSubclass(_hwnd, s_subclassProc, SubclassId);
            if (_capturingHotkey)
            {
                App.Current.Hotkey.Resume();
            }
        };
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        Activated += (_, e) =>
        {
            // 別のウィンドウに切り替えた場合はホットキーの入力受付を終了する
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                HotkeyInput.CancelCapture();
            }
        };

        ApplyTheme();
        ResizeAndCenter();
    }

    /// <summary>ViewModel</summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// ウィンドウを表示して前面に出す。
    /// </summary>
    public void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        WindowHelper.BringToForeground(_hwnd);
    }

    private static LRESULT SubclassProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (message == PInvoke.WM_SYSCOMMAND && ((uint)wParam.Value & 0xFFF0) == PInvoke.SC_KEYMENU && IsCapturing(hwnd))
        {
            return new LRESULT(0);
        }

        return PInvoke.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static bool IsCapturing(HWND hwnd) =>
        App.Current.OpenedSettingsWindow is { } window && window._hwnd == hwnd && window._capturingHotkey;

    private void OnSettingsChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = App.Current.RequestedElementTheme;
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x0A, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x06, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = Colors.Gray;
    }

    private void ResizeAndCenter()
    {
        var monitor = WindowHelper.GetMonitorAtCursor();
        var width = (int)(DefaultWidth * monitor.Scale);
        var height = (int)(DefaultHeight * monitor.Scale);
        var work = monitor.WorkArea;
        var rect = new RectInt32(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height);
        rect = WindowHelper.FitToWorkArea(rect, work);
        AppWindow.MoveAndResize(rect);
        AppWindow.MoveAndResize(rect);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = Bind.VisibleIf(tag == "General");
        AppearancePage.Visibility = Bind.VisibleIf(tag == "Appearance");
        AboutPage.Visibility = Bind.VisibleIf(tag == "About");
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            using var process = Process.Start(new ProcessStartInfo { FileName = AppPaths.DataDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log.Error("データフォルダを開けませんでした。", ex);
        }
    }

    private void ClearIconCache_Click(object sender, RoutedEventArgs e) => App.Current.ClearIconCache();
}
