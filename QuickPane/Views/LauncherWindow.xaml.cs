using System.ComponentModel;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuickPane.Core.Models;
using QuickPane.Helpers;
using QuickPane.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace QuickPane.Views;

/// <summary>
/// ランチャー画面。起動時に 1 度だけ生成し、以降は表示・非表示を切り替える。
/// </summary>
public sealed partial class LauncherWindow : Window
{
    private const double MinWidth = 400;

    private const double MinResizeWidth = 300;

    /// <summary>ホイール 1 ノッチ分の回転量</summary>
    private const int WheelDelta = 120;

    private static readonly nuint SubclassId = 1;

    private const double ChromeWidth = 28;

    private const double ChromeHeight = 70;

    private const double TileMargin = 6;

    private const int MinRows = 2;

    private const int MaxRows = 5;

    private const string ItemDataFormat = "QuickPane.Item";

    /// <summary>非表示にしてからメモリを解放するまでの時間</summary>
    private static readonly TimeSpan TrimDelay = TimeSpan.FromSeconds(30);

    private readonly HWND _hwnd;

    private readonly LauncherBackdrop? _backdrop;

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _trimTimer;

    private int _modalDepth;

    private int _iconPixelSize;

    private int _visibilityVersion;

    private bool _isLauncherVisible;

    private bool _exiting;

    private ItemViewModel? _draggedItem;

    private TabViewModel? _selectedTabBeforeDrag;

    private bool _isDraggingWindow;

    private PointInt32 _dragStartCursor;

    private PointInt32 _dragStartWindow;

    private SizeInt32 _sizeBeforeResize;

    private int _tabWheelDelta;

    private static LauncherWindow? s_instance;

    private static SUBCLASSPROC? s_subclassProc;

    internal LauncherWindow(LauncherViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        _hwnd = WindowHelper.GetHandle(this);

        // タイトルバーなし (端のドラッグでサイズ変更可)、常に最前面、タスクバー・Alt + Tab に表示しない
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        WindowHelper.SetToolWindow(_hwnd);
        WindowHelper.SetRoundedCorners(_hwnd);

        // サイズ変更の開始・終了と最小サイズの処理
        s_instance = this;
        s_subclassProc ??= SubclassProc;
        PInvoke.SetWindowSubclass(_hwnd, s_subclassProc, SubclassId, 0);
        if (!WindowHelper.IsWindows11)
        {
            GlassLayer.CornerRadius = new CornerRadius(0);
        }
        else
        {
            GlassLayer.CornerRadius = new CornerRadius(8);
        }

        // 背景はアクリル。使用できない環境では単色で表示する
        if (LauncherBackdrop.IsSupported)
        {
            _backdrop = new LauncherBackdrop();
            SystemBackdrop = _backdrop;
        }
        else
        {
            RootGrid.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        }

        AppWindow.Closing += (_, e) =>
        {
            // Alt + F4 等では終了せず非表示にする
            if (!_exiting)
            {
                e.Cancel = true;
                HideLauncher();
            }
        };
        Activated += OnActivated;
        RootGrid.ActualThemeChanged += (_, _) => _backdrop?.SetTheme(RootGrid.ActualTheme);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ItemsChanged += (_, _) => UpdateWindowRect(keepPosition: true);
        App.Current.SettingsChanged += OnSettingsChanged;
        ContentRoot.SizeChanged += (_, _) => UpdateItemGridWidth();

        // タブ欄のホイール操作はタブの横スクロールに使われるため、処理済みのイベントも受け取る
        TabList.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(TabList_PointerWheelChanged), true);

        // 非表示のまましばらく経過したらメモリを解放する
        _trimTimer = DispatcherQueue.CreateTimer();
        _trimTimer.Interval = TrimDelay;
        _trimTimer.IsRepeating = false;
        _trimTimer.Tick += (_, _) =>
        {
            if (!_isLauncherVisible)
            {
                MemoryHelper.Trim();
            }
        };

        ApplyTheme();
        ApplyColumns();
    }

    /// <summary>ViewModel</summary>
    public LauncherViewModel ViewModel { get; }

    /// <summary>
    /// 起動時の準備。画面外で 1 度表示して描画を初期化し、初回表示時の待ちをなくす。
    /// </summary>
    public void Prepare()
    {
        AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 480, 320));
        AppWindow.Show(false);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_isLauncherVisible)
            {
                AppWindow.Hide();
                _trimTimer.Start();
            }
        });
    }

    /// <summary>
    /// 表示・非表示を切り替える (ホットキー)。
    /// </summary>
    public void Toggle()
    {
        if (_isLauncherVisible)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    /// <summary>
    /// ランチャーを表示する。
    /// </summary>
    public void ShowLauncher()
    {
        _visibilityVersion++;
        _trimTimer.Stop();
        ViewModel.IsErrorOpen = false;

        if (!_isLauncherVisible)
        {
            UpdateWindowRect(keepPosition: false);
            _isLauncherVisible = true;
            AnimateIn();
        }

        AppWindow.Show(true);
        Activate();
        WindowHelper.BringToForeground(_hwnd);
        FocusItems();
    }

    /// <summary>
    /// ランチャーを非表示にする。
    /// </summary>
    public async void HideLauncher()
    {
        if (!_isLauncherVisible)
        {
            return;
        }

        _isLauncherVisible = false;
        var version = ++_visibilityVersion;
        AnimateOut();
        await Task.Delay(90);

        // アニメーション中に再表示された場合は非表示にしない
        if (version == _visibilityVersion)
        {
            AppWindow.Hide();
            _trimTimer.Start();
        }
    }

    /// <summary>
    /// アプリ終了時にウィンドウを閉じる。
    /// </summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    private void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        // ランチャー外をクリックした (フォーカスが外れた) 場合は閉じる
        if (args.WindowActivationState == WindowActivationState.Deactivated
            && _isLauncherVisible && _modalDepth == 0 && !ViewModel.IsPinned)
        {
            HideLauncher();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherViewModel.SelectedTab) && _isLauncherVisible)
        {
            FocusItems();
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyTheme();
        ApplyColumns();
        if (ViewModel.Layout.IconPixelSize != _iconPixelSize)
        {
            ViewModel.ReloadIcons();
        }

        UpdateWindowRect(keepPosition: true);
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = App.Current.RequestedElementTheme;
        _backdrop?.SetTheme(RootGrid.ActualTheme);
    }

    private void ApplyColumns()
    {
        _iconPixelSize = ViewModel.Layout.IconPixelSize;
        if (ItemGrid.ItemsPanelRoot is null)
        {
            ItemGrid.Loaded += (_, _) => UpdateItemGridWidth();
        }

        UpdateItemGridWidth();
    }

    /// <summary>
    /// アイテム一覧の列数・幅を設定する。
    /// 自動サイズの場合は設定の列数、サイズを変更した場合はウィンドウの幅に収まる列数とし、
    /// 列数分の幅に固定してアイテムが少ない場合も左詰めで表示する。
    /// </summary>
    private void UpdateItemGridWidth()
    {
        var layout = ViewModel.Layout;
        var tileWidth = layout.TileWidth + TileMargin;
        var columns = layout.Columns;
        if (App.Current.Settings.HasLauncherSize && ContentRoot.ActualWidth > 0)
        {
            var available = ContentRoot.ActualWidth - ContentRoot.Padding.Left - ContentRoot.Padding.Right - 2;
            columns = Math.Max(1, (int)Math.Floor(available / tileWidth));
        }

        ItemGrid.Width = (columns * tileWidth) + 2;
        if (ItemGrid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.MaximumRowsOrColumns = columns;
        }
    }

    /// <summary>
    /// ウィンドウのサイズ (DIP) を取得する。サイズを変更している場合はその値、それ以外は列数・アイテム数から計算する。
    /// </summary>
    private (double Width, double Height) CalculateSize()
    {
        var settings = App.Current.Settings;
        var (minWidth, minHeight) = MinimumSize;
        if (settings is { LauncherWidth: { } customWidth, LauncherHeight: { } customHeight })
        {
            return (Math.Max(minWidth, customWidth), Math.Max(minHeight, customHeight));
        }

        var layout = ViewModel.Layout;
        var width = Math.Max(MinWidth, (layout.Columns * (layout.TileWidth + TileMargin)) + ChromeWidth);
        var rows = Math.Clamp((int)Math.Ceiling(ViewModel.MaxItemCount / (double)layout.Columns), MinRows, MaxRows);
        var height = (rows * (layout.TileHeight + TileMargin)) + ChromeHeight;
        return (width, height);
    }

    /// <summary>端のドラッグで変更できる最小サイズ (DIP、クライアント領域)</summary>
    private (double Width, double Height) MinimumSize =>
        (MinResizeWidth, ViewModel.Layout.TileHeight + TileMargin + ChromeHeight);

    // ---- 端のドラッグによるサイズ変更 ------------------------------------------------------

    private static unsafe LRESULT SubclassProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (s_instance is { } window && window._hwnd == hwnd)
        {
            switch (message)
            {
                case PInvoke.WM_ENTERSIZEMOVE:
                    window._sizeBeforeResize = window.AppWindow.Size;
                    break;
                case PInvoke.WM_EXITSIZEMOVE:
                    window.OnResized();
                    break;
                case PInvoke.WM_GETMINMAXINFO:
                    var result = PInvoke.DefSubclassProc(hwnd, message, wParam, lParam);
                    window.SetMinimumTrackSize((MINMAXINFO*)lParam.Value);
                    return result;
            }
        }

        return PInvoke.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private unsafe void SetMinimumTrackSize(MINMAXINFO* info)
    {
        var scale = PInvoke.GetDpiForWindow(_hwnd) / 96.0;
        var frame = WindowHelper.GetFrameSize(_hwnd);
        var (minWidth, minHeight) = MinimumSize;
        info->ptMinTrackSize.X = (int)Math.Ceiling(minWidth * scale) + frame.Width;
        info->ptMinTrackSize.Y = (int)Math.Ceiling(minHeight * scale) + frame.Height;
    }

    private void OnResized()
    {
        if (!_isLauncherVisible || _modalDepth > 0 || AppWindow.Size == _sizeBeforeResize)
        {
            return;
        }

        // 変更後のサイズ (クライアント領域、DIP) と位置を保存する (左・上の端で変更した場合は位置も変わる)
        var scale = PInvoke.GetDpiForWindow(_hwnd) / 96.0;
        var frame = WindowHelper.GetFrameSize(_hwnd);
        var width = (AppWindow.Size.Width - frame.Width) / scale;
        var height = (AppWindow.Size.Height - frame.Height) / scale;
        var (offsetX, offsetY) = WindowHelper.GetClientOffset(_hwnd);
        var left = AppWindow.Position.X + offsetX;
        var top = AppWindow.Position.Y + offsetY;
        App.Current.UpdateSettings(s =>
        {
            (s.LauncherWidth, s.LauncherHeight) = (Math.Round(width), Math.Round(height));
            (s.LastLeft, s.LastTop) = (left, top);
        });
    }

    // ---- 画面上部のドラッグによる移動 ------------------------------------------------------

    private void DragArea_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(DragArea).Properties.IsLeftButtonPressed || !DragArea.CapturePointer(e.Pointer))
        {
            return;
        }

        e.Handled = true;
        _dragStartCursor = WindowHelper.GetCursorPosition();
        _dragStartWindow = AppWindow.Position;
        _isDraggingWindow = true;
    }

    private void DragArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingWindow)
        {
            return;
        }

        // 座標は物理ピクセルで扱う (拡大率の異なるモニタへ移動した場合もずれないようにする)
        var cursor = WindowHelper.GetCursorPosition();
        AppWindow.Move(new PointInt32(
            _dragStartWindow.X + cursor.X - _dragStartCursor.X,
            _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y));
        e.Handled = true;
    }

    private void DragArea_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        DragArea.ReleasePointerCapture(e.Pointer);
        EndWindowDrag();
    }

    private void DragArea_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndWindowDrag();

    private void EndWindowDrag()
    {
        if (!_isDraggingWindow)
        {
            return;
        }

        _isDraggingWindow = false;
        if (AppWindow.Position == _dragStartWindow)
        {
            return;
        }

        // 画面からはみ出した部分を戻し、移動した位置 (クライアント領域の左上) を保存する
        UpdateWindowRect(keepPosition: true);
        var (offsetX, offsetY) = WindowHelper.GetClientOffset(_hwnd);
        var left = AppWindow.Position.X + offsetX;
        var top = AppWindow.Position.Y + offsetY;
        App.Current.UpdateSettings(s => (s.LastLeft, s.LastTop) = (left, top));
    }

    /// <summary>
    /// ウィンドウの位置・サイズを設定する。マウスカーソル付近 (画面中央・前回の位置) に、画面内に収まるよう配置する。
    /// </summary>
    /// <param name="keepPosition">現在の位置を基準にサイズのみ変更する場合は true</param>
    private void UpdateWindowRect(bool keepPosition)
    {
        if (keepPosition && !_isLauncherVisible)
        {
            return;
        }

        var settings = App.Current.Settings;
        PointInt32? last = !keepPosition && settings.Position == LauncherPosition.Last && settings is { LastLeft: { } left, LastTop: { } top }
            ? new PointInt32(left, top)
            : null;
        var monitor = keepPosition ? WindowHelper.GetMonitorForWindow(_hwnd)
            : last is { } lastPoint ? WindowHelper.GetMonitorAt(lastPoint)
            : WindowHelper.GetMonitorAtCursor();
        var (offsetX, offsetY) = WindowHelper.GetClientOffset(_hwnd);
        var current = new PointInt32(AppWindow.Position.X + offsetX, AppWindow.Position.Y + offsetY);
        if (!keepPosition)
        {
            // 先に表示先のモニタへ移動し、モニタの拡大率 (DPI) を反映させてからサイズを設定する
            AppWindow.Move(monitor.Cursor);
        }

        var (widthDip, heightDip) = CalculateSize();
        var width = (int)Math.Ceiling(widthDip * monitor.Scale);
        var height = (int)Math.Ceiling(heightDip * monitor.Scale);
        var work = monitor.WorkArea;

        // 位置・サイズはクライアント領域 (見えないリサイズ枠を除いた表示部分) で計算する
        int x, y;
        if (keepPosition)
        {
            (x, y) = (current.X, current.Y);
        }
        else if (last is { } point)
        {
            (x, y) = (point.X, point.Y);
        }
        else if (settings.Position == LauncherPosition.Center)
        {
            x = work.X + ((work.Width - width) / 2);
            y = work.Y + ((work.Height - height) / 2);
        }
        else
        {
            x = monitor.Cursor.X - (width / 2);
            y = monitor.Cursor.Y - (int)(24 * monitor.Scale);
        }

        var rect = WindowHelper.FitToWorkArea(new RectInt32(x, y, width, height), work);
        var frame = WindowHelper.GetFrameSize(_hwnd);
        (offsetX, offsetY) = WindowHelper.GetClientOffset(_hwnd);
        AppWindow.MoveAndResize(new RectInt32(rect.X - offsetX, rect.Y - offsetY, rect.Width + frame.Width, rect.Height + frame.Height));
    }

    private void AnimateIn()
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ContentRoot);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)(ContentRoot.ActualWidth / 2), 0, 0);

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f);
        opacity.Duration = TimeSpan.FromMilliseconds(130);

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.97f, 0.97f, 1f));
        scale.InsertKeyFrame(1f, Vector3.One, easing);
        scale.Duration = TimeSpan.FromMilliseconds(180);

        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Scale", scale);
    }

    private void AnimateOut()
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ContentRoot);
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, 0f);
        opacity.Duration = TimeSpan.FromMilliseconds(80);
        visual.StartAnimation("Opacity", opacity);
    }

    /// <summary>
    /// アイテムにフォーカスを移す (矢印キー・Enter で操作できるよう、先頭のアイテムを選択する)。
    /// </summary>
    private void FocusItems()
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ItemGrid.Items.Count == 0)
            {
                // アイテムがない場合は選択中のタブにフォーカスを置く (矢印キーでタブを切り替えられる)
                (TabList.ContainerFromIndex(TabList.SelectedIndex) as Control)?.Focus(FocusState.Programmatic);
                return;
            }

            if (ItemGrid.SelectedIndex < 0)
            {
                ItemGrid.SelectedIndex = 0;
            }

            ItemGrid.ScrollIntoView(ItemGrid.SelectedItem);
            if (ItemGrid.ContainerFromIndex(ItemGrid.SelectedIndex) is GridViewItem container)
            {
                container.Focus(FocusState.Programmatic);
            }
            else
            {
                ItemGrid.Focus(FocusState.Programmatic);
            }
        });
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_modalDepth > 0)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                HideLauncher();
                e.Handled = true;
                break;
            case VirtualKey.Tab when IsKeyDown(VirtualKey.Control):
                ViewModel.SelectNextTab(IsKeyDown(VirtualKey.Shift) ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    private void ItemGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ItemGrid.SelectedItem is ItemViewModel item)
        {
            e.Handled = true;
            _ = LaunchAsync(item);
        }
    }

    private void ItemGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemViewModel item)
        {
            _ = LaunchAsync(item);
        }
    }

    private async Task LaunchAsync(ItemViewModel item, bool? runAsAdmin = null)
    {
        var result = await ViewModel.LaunchAsync(item, runAsAdmin);
        if (result.Success)
        {
            if (App.Current.Settings.CloseAfterLaunch)
            {
                HideLauncher();
            }
        }
        else if (!result.Cancelled)
        {
            ShowLaunchError($"「{item.Name}」を起動できませんでした", result.ErrorMessage);
        }
    }

    private void ShowLaunchError(string title, string? message)
    {
        if (_isLauncherVisible)
        {
            ViewModel.ShowError(title, message ?? string.Empty);
        }
        else
        {
            // 起動に時間がかかり、その間にランチャーが閉じられた場合はトーストで通知する
            App.Current.Notify(title, message ?? string.Empty, isError: true);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        HideLauncher();
        App.Current.OpenSettings();
    }

    private async void AddTabButton_Click(object sender, RoutedEventArgs e) => await AddTabAsync();

    private async Task AddTabAsync()
    {
        var name = await ShowTextInputAsync("タブを追加", "タブ名", string.Empty, "追加");
        if (name is not null)
        {
            ViewModel.AddTab(name);
        }
    }

    // ---- 右クリックメニュー ----------------------------------------------------------------

    private void ItemGrid_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        // キーボード (Shift + F10 等) の場合は選択中のアイテムを対象とする
        var item = e.TryGetPosition(sender, out _)
            ? FindDataContext<ItemViewModel>(e.OriginalSource as DependencyObject, ItemGrid)
            : ItemGrid.SelectedItem as ItemViewModel;
        if (item is not null)
        {
            ItemGrid.SelectedItem = item;
        }

        var menu = item is null ? CreateBackgroundMenu() : CreateItemMenu(item);
        ShowMenu(menu, sender, e);
    }

    private void TabList_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var tab = e.TryGetPosition(sender, out _)
            ? FindDataContext<TabViewModel>(e.OriginalSource as DependencyObject, TabList)
            : TabList.SelectedItem as TabViewModel;
        var menu = new MenuFlyout();
        if (tab is null)
        {
            menu.Items.Add(CreateMenuItem("タブを追加", "", async () => await AddTabAsync()));
        }
        else
        {
            menu.Items.Add(CreateMenuItem("タブ名を変更", "", async () =>
            {
                var name = await ShowTextInputAsync("タブ名を変更", "タブ名", tab.Name, "変更");
                if (name is not null)
                {
                    ViewModel.RenameTab(tab, name);
                }
            }));
            var delete = CreateMenuItem("タブを削除", "", async () => await DeleteTabAsync(tab));
            delete.IsEnabled = ViewModel.Tabs.Count > 1;
            menu.Items.Add(delete);
        }

        ShowMenu(menu, sender, e);
    }

    /// <summary>
    /// イベント発生元の要素からリストの項目 (ViewModel) を取得する。
    /// </summary>
    private static T? FindDataContext<T>(DependencyObject? source, ListViewBase list)
        where T : class
    {
        while (source is not null && source != list)
        {
            if (source is SelectorItem container)
            {
                return list.ItemFromContainer(container) as T;
            }

            if (source is FrameworkElement { DataContext: T value })
            {
                return value;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private MenuFlyout CreateItemMenu(ItemViewModel item)
    {
        var kind = item.Model.Kind;
        var menu = new MenuFlyout();
        menu.Items.Add(CreateMenuItem("開く", "", () => _ = LaunchAsync(item)));

        var admin = CreateMenuItem("管理者として実行", "", () => _ = LaunchAsync(item, runAsAdmin: true));
        admin.IsEnabled = kind is ItemKind.App or ItemKind.File;
        menu.Items.Add(admin);

        var location = CreateMenuItem("ファイルの場所を開く", "", async () =>
        {
            var result = await ViewModel.OpenLocationAsync(item);
            if (!result.Success)
            {
                ShowLaunchError($"「{item.Name}」の場所を開けませんでした", result.ErrorMessage);
            }
        });
        location.IsEnabled = kind != ItemKind.Url;
        menu.Items.Add(location);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateMenuItem("編集", "", async () => await EditItemAsync(item)));

        var move = new MenuFlyoutSubItem { Text = "別のタブへ移動", Icon = new FontIcon { Glyph = "" } };
        var current = ViewModel.FindTab(item);
        foreach (var tab in ViewModel.Tabs.Where(t => t != current))
        {
            var target = tab;
            move.Items.Add(CreateMenuItem(tab.Name, null, () => ViewModel.MoveItem(item, target)));
        }

        move.IsEnabled = move.Items.Count > 0;
        menu.Items.Add(move);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateMenuItem("削除", "", () => ViewModel.DeleteItem(item)));
        return menu;
    }

    private MenuFlyout CreateBackgroundMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateMenuItem("アイテムを追加", "", async () => await AddItemAsync()));
        menu.Items.Add(CreateMenuItem("タブを追加", "", async () => await AddTabAsync()));
        var resetSize = CreateMenuItem("ウィンドウのサイズを自動に戻す", "\uE9A6", () => App.Current.UpdateSettings(s => s.ResetLauncherSize()));
        resetSize.IsEnabled = App.Current.Settings.HasLauncherSize;
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(resetSize);
        return menu;
    }

    private static MenuFlyoutItem CreateMenuItem(string text, string? glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (glyph is not null)
        {
            item.Icon = new FontIcon { Glyph = glyph };
        }

        item.Click += (_, _) => action();
        return item;
    }

    private void ShowMenu(MenuFlyout menu, UIElement sender, ContextRequestedEventArgs e)
    {
        // メニュー表示中はフォーカスが外れてもランチャーを閉じない
        _modalDepth++;
        menu.Closed += (_, _) => _modalDepth--;

        if (e.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt(e.OriginalSource as FrameworkElement ?? (FrameworkElement)sender);
        }

        e.Handled = true;
    }

    // ---- ダイアログ ------------------------------------------------------------------------

    private async Task AddItemAsync()
    {
        var tab = ViewModel.SelectedTab;
        if (tab is null)
        {
            return;
        }

        var editor = new ItemEditViewModel(new LauncherItem(), isNew: true);
        if (await ShowItemEditDialogAsync(editor))
        {
            var item = ViewModel.AddItem(editor.ToItem(), tab);
            ItemGrid.SelectedItem = item;
        }
    }

    private async Task EditItemAsync(ItemViewModel item)
    {
        var editor = new ItemEditViewModel(item.Model, isNew: false);
        if (await ShowItemEditDialogAsync(editor))
        {
            ViewModel.UpdateItem(item, editor.ToItem());
        }
    }

    private async Task<bool> ShowItemEditDialogAsync(ItemEditViewModel editor)
    {
        // ダイアログが収まるよう、表示中のみウィンドウを広げる
        var original = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        var monitor = WindowHelper.GetMonitorForWindow(_hwnd);
        var width = Math.Max(original.Width, (int)(600 * monitor.Scale));
        var height = Math.Max(original.Height, (int)(640 * monitor.Scale));
        var expanded = WindowHelper.FitToWorkArea(
            new RectInt32(original.X - ((width - original.Width) / 2), original.Y, width, height),
            monitor.WorkArea);
        AppWindow.MoveAndResize(expanded);

        try
        {
            var dialog = new ItemEditDialog(editor, _hwnd);
            return await ShowDialogAsync(dialog) == ContentDialogResult.Primary;
        }
        finally
        {
            AppWindow.MoveAndResize(original);
        }
    }

    private async Task DeleteTabAsync(TabViewModel tab)
    {
        var dialog = new ContentDialog
        {
            Title = "タブを削除",
            Content = tab.Items.Count == 0
                ? $"タブ「{tab.Name}」を削除しますか?"
                : $"タブ「{tab.Name}」を削除しますか?\nタブ内のアイテム ({tab.Items.Count} 件) もすべて削除されます。",
            PrimaryButtonText = "削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            ViewModel.DeleteTab(tab);
        }
    }

    private async Task<string?> ShowTextInputAsync(string title, string header, string initialText, string primaryText)
    {
        var textBox = new TextBox { Header = header, Text = initialText, MaxLength = 50 };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryText,
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(initialText),
        };
        textBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(textBox.Text);
        textBox.Loaded += (_, _) =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        };

        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary ? textBox.Text.Trim() : null;
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = RootGrid.XamlRoot;
        dialog.RequestedTheme = RootGrid.ActualTheme;
        dialog.Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];

        // ダイアログ表示中 (ファイル選択画面を含む) はフォーカスが外れてもランチャーを閉じない
        _modalDepth++;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _modalDepth--;
            FocusItems();
        }
    }

    // ---- ドラッグ＆ドロップ ----------------------------------------------------------------

    private void ItemGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _draggedItem = e.Items.FirstOrDefault() as ItemViewModel;
        if (_draggedItem is not null)
        {
            e.Data.SetData(ItemDataFormat, _draggedItem.Model.Id.ToString());
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void ItemGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _draggedItem = null;

        // 並べ替え・タブ間の移動結果を保存する
        ViewModel.Save();
    }

    private void ItemGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "このタブに登録";
        }
    }

    private async void ItemGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || ViewModel.SelectedTab is not { } tab)
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            await ViewModel.AddPathsAsync(await GetDroppedPathsAsync(e.DataView), tab);
        }
        catch (Exception ex)
        {
            App.Log.Error("ドロップされたファイルを登録できませんでした。", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab } element)
        {
            return;
        }

        if (_draggedItem is not null && e.DataView.Contains(ItemDataFormat))
        {
            if (ViewModel.FindTab(_draggedItem) == tab)
            {
                return;
            }

            e.AcceptedOperation = DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"「{tab.Name}」へ移動";
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = $"「{tab.Name}」に登録";
        }
        else
        {
            return;
        }

        if (element is Panel panel)
        {
            var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            panel.Background = new SolidColorBrush(accent) { Opacity = 0.3 };
        }

        e.Handled = true;
    }

    private void Tab_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Panel panel)
        {
            panel.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private async void Tab_Drop(object sender, DragEventArgs e)
    {
        Tab_DragLeave(sender, e);
        if (sender is not FrameworkElement { DataContext: TabViewModel tab })
        {
            return;
        }

        e.Handled = true;
        if (_draggedItem is not null && e.DataView.Contains(ItemDataFormat))
        {
            ViewModel.MoveItem(_draggedItem, tab);
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var deferral = e.GetDeferral();
            try
            {
                await ViewModel.AddPathsAsync(await GetDroppedPathsAsync(e.DataView), tab);
                ViewModel.SelectedTabIndex = ViewModel.Tabs.IndexOf(tab);
            }
            catch (Exception ex)
            {
                App.Log.Error("ドロップされたファイルを登録できませんでした。", ex);
            }
            finally
            {
                deferral.Complete();
            }
        }
    }

    /// <summary>
    /// タブ欄の上でホイールを回した場合、タブを切り替える (上: 前のタブ、下: 次のタブ。端では止める)。
    /// </summary>
    private void TabList_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(TabList).Properties;
        if (properties.IsHorizontalMouseWheel || ViewModel.Tabs.Count == 0)
        {
            return;
        }

        // タッチパッド等の細かい回転量は 1 ノッチ分 (120) たまってから切り替える
        _tabWheelDelta += properties.MouseWheelDelta;
        var steps = _tabWheelDelta / WheelDelta;
        if (steps == 0)
        {
            return;
        }

        _tabWheelDelta -= steps * WheelDelta;
        var index = Math.Clamp(ViewModel.SelectedTabIndex - steps, 0, ViewModel.Tabs.Count - 1);
        if (index != ViewModel.SelectedTabIndex)
        {
            ViewModel.SelectedTabIndex = index;
            TabList.ScrollIntoView(TabList.SelectedItem);
        }
    }

    private void TabList_DragItemsStarting(object sender, DragItemsStartingEventArgs e) =>
        _selectedTabBeforeDrag = ViewModel.SelectedTab;

    private void TabList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        ViewModel.CompleteTabReorder(_selectedTabBeforeDrag);
        _selectedTabBeforeDrag = null;
    }

    private static async Task<IReadOnlyList<string>> GetDroppedPathsAsync(DataPackageView data)
    {
        var items = await data.GetStorageItemsAsync();
        return items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    // ---- タイルのホバー・押下アニメーション ------------------------------------------------

    private static void SetTileState(object sender, float scale, double sheenOpacity)
    {
        if (sender is not Grid tile)
        {
            return;
        }

        tile.CenterPoint = new Vector3((float)(tile.ActualWidth / 2), (float)(tile.ActualHeight / 2), 0);
        tile.Scale = new Vector3(scale, scale, 1);
        if (tile.Children.Count > 0)
        {
            tile.Children[0].Opacity = sheenOpacity;
        }
    }

    private void Tile_PointerEntered(object sender, PointerRoutedEventArgs e) => SetTileState(sender, 1.05f, 1);

    private void Tile_PointerExited(object sender, PointerRoutedEventArgs e) => SetTileState(sender, 1f, 0);

    private void Tile_PointerPressed(object sender, PointerRoutedEventArgs e) => SetTileState(sender, 0.96f, 1);

    private void Tile_PointerReleased(object sender, PointerRoutedEventArgs e) => SetTileState(sender, 1.05f, 1);
}
