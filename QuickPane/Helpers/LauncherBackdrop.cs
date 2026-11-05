using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace QuickPane.Helpers;

/// <summary>
/// ランチャー用のアクリル背景。かすかに背景が透けるよう色味・透明度を調整し、非アクティブ時もアクリルを維持する。
/// </summary>
internal sealed class LauncherBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    private SystemBackdropConfiguration? _configuration;

    private ElementTheme _theme = ElementTheme.Light;

    /// <summary>アクリルが使用可能かどうか</summary>
    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    /// <summary>
    /// テーマを設定する。
    /// </summary>
    /// <param name="theme">実際のテーマ (Light / Dark)</param>
    public void SetTheme(ElementTheme theme)
    {
        _theme = theme;
        Apply();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        Apply();
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);

        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }

    private void Apply()
    {
        if (_controller is null || _configuration is null)
        {
            return;
        }

        var dark = _theme == ElementTheme.Dark;
        _configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _controller.TintColor = dark ? Color.FromArgb(255, 0x1E, 0x1F, 0x26) : Color.FromArgb(255, 0xF4, 0xF6, 0xFB);
        _controller.TintOpacity = dark ? 0.38f : 0.3f;
        _controller.LuminosityOpacity = dark ? 0.78f : 0.7f;
        _controller.FallbackColor = dark ? Color.FromArgb(255, 0x24, 0x25, 0x2C) : Color.FromArgb(255, 0xF3, 0xF4, 0xF8);
    }
}
