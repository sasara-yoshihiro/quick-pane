using System.Reflection;
using log4net;
using Microsoft.UI.Xaml;
using QuickPane.Core.Configuration;
using QuickPane.Core.Icons;
using QuickPane.Core.Models;
using QuickPane.Core.Storage;
using QuickPane.Services;
using QuickPane.ViewModels;
using QuickPane.Views;

namespace QuickPane;

/// <summary>
/// アプリケーション本体。常駐 (トレイ・ホットキー) とウィンドウ・設定を管理する。
/// </summary>
public partial class App : Application
{
    /// <summary>ロガー</summary>
    internal static readonly ILog Log = LogManager.GetLogger("QuickPane");

    private MessageWindow? _messageWindow;

    private TrayIcon? _trayIcon;

    private HotkeyService? _hotkeyService;

    private LauncherWindow? _launcherWindow;

    private SettingsWindow? _settingsWindow;

    private bool _exiting;

    public App()
    {
        AppConfig.ConfigureLogging();
        InitializeComponent();

        // ウィンドウをすべて閉じても (非表示にしても) 終了しない
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        UnhandledException += (_, e) =>
        {
            Log.Error("未処理の例外が発生しました。", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal("未処理の例外が発生しました。", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("未処理のタスク例外が発生しました。", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>現在のアプリケーション</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>設定画面で変更する設定</summary>
    internal UserSettings Settings { get; private set; } = new();

    /// <summary>アイコン取得</summary>
    internal IconService Icons { get; } = new(AppPaths.IconCacheDirectory, TimeSpan.FromSeconds(5));

    /// <summary>ランチャーのレイアウト (アイコンサイズ・列数)</summary>
    internal LauncherLayout Layout { get; } = new();

    /// <summary>ホットキー</summary>
    internal HotkeyService Hotkey => _hotkeyService!;

    /// <summary>表示中の設定画面 (表示していない場合は null)</summary>
    internal SettingsWindow? OpenedSettingsWindow => _settingsWindow;

    /// <summary>アプリのバージョン</summary>
    internal static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>設定が変更された</summary>
    internal event EventHandler? SettingsChanged;

    /// <summary>
    /// 設定のテーマに対応する ElementTheme。
    /// </summary>
    internal ElementTheme RequestedElementTheme => Settings.Theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Info($"起動しました。バージョン={Version}");

        var settingsResult = DataStore.LoadSettings();
        var itemsResult = DataStore.LoadItems();
        Settings = settingsResult.Value;
        Layout.Apply(Settings);

        _messageWindow = new MessageWindow();
        _messageWindow.AddHandler((message, _, _) =>
        {
            if (message != MessageWindow.ShowLauncherMessage)
            {
                return false;
            }

            ShowLauncher();
            return true;
        });

        TrayIcon.ApplyMenuTheme(Settings.Theme);
        _trayIcon = new TrayIcon(_messageWindow);
        _trayIcon.ShowLauncherRequested += (_, _) => ShowLauncher();
        _trayIcon.SettingsRequested += (_, _) => OpenSettings();
        _trayIcon.ExitRequested += (_, _) => Shutdown();
        _trayIcon.Show();

        // ランチャーは起動時に 1 度だけ生成し、以降は表示・非表示のみ切り替える
        var launcherViewModel = new LauncherViewModel(itemsResult.Value, Icons, Layout);
        _launcherWindow = new LauncherWindow(launcherViewModel);
        _launcherWindow.Prepare();

        _hotkeyService = new HotkeyService(_messageWindow);
        _hotkeyService.Pressed += (_, _) => _launcherWindow.Toggle();
        RegisterHotkey();

        NotifyLoadResult(settingsResult.Status, "設定", settingsResult.CorruptFilePath);
        NotifyLoadResult(itemsResult.Status, "登録アイテム", itemsResult.CorruptFilePath);

        AutoStartManager.RefreshPath();
    }

    /// <summary>
    /// ランチャーを表示する。
    /// </summary>
    internal void ShowLauncher() => _launcherWindow?.ShowLauncher();

    /// <summary>
    /// 設定画面を表示する。
    /// </summary>
    internal void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.ShowAndActivate();
    }

    /// <summary>
    /// 設定を変更して保存し、変更を通知する。
    /// </summary>
    /// <param name="change">変更処理</param>
    internal void UpdateSettings(Action<UserSettings> change)
    {
        var previousTheme = Settings.Theme;
        change(Settings);
        Settings.Normalize();
        DataStore.SaveSettings(Settings);
        Layout.Apply(Settings);

        if (previousTheme != Settings.Theme)
        {
            TrayIcon.ApplyMenuTheme(Settings.Theme);
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// アイコンキャッシュを削除し、ランチャーのアイコンを再取得する。
    /// </summary>
    internal void ClearIconCache()
    {
        Icons.ClearCache();
        _launcherWindow?.ViewModel.ReloadIcons();
    }

    /// <summary>
    /// トースト (バルーン) 通知を表示する。
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="isError">エラー・警告の場合は true</param>
    /// <param name="clickAction">通知がクリックされたときの処理</param>
    internal void Notify(string title, string message, bool isError, Action? clickAction = null) =>
        _trayIcon?.Notify(title, message, isError, clickAction);

    /// <summary>
    /// アプリを終了する。
    /// </summary>
    internal void Shutdown()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Log.Info("終了しました。");

        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        _launcherWindow?.CloseForExit();
        _messageWindow?.Dispose();
        Exit();
    }

    private void RegisterHotkey()
    {
        HotkeyGesture.TryParse(Settings.Hotkey, out var gesture);
        if (!_hotkeyService!.Register(gesture))
        {
            Notify(
                "ホットキーを登録できませんでした",
                $"{gesture.ToDisplayString()} は他のアプリで使用されている可能性があります。この通知をクリックして、設定画面からホットキーを変更してください。",
                isError: true,
                OpenSettings);
        }
    }

    private void NotifyLoadResult(LoadStatus status, string name, string? corruptFilePath)
    {
        if (status == LoadStatus.Reset)
        {
            var moved = corruptFilePath is null ? string.Empty : $"破損したファイルは {Path.GetFileName(corruptFilePath)} として退避しました。";
            Notify($"{name}を読み込めませんでした", $"ファイルが破損しているため、初期状態で起動しました。{moved}", isError: true);
        }
    }
}
