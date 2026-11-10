using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using QuickPane.Services;

namespace QuickPane;

/// <summary>
/// エントリポイント。二重起動の場合は起動中のランチャーを表示して終了する。
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 管理者として実行中の QuickPane から、アイテムを通常の権限で起動するための起動用プロセスとして起動された場合
        if (args.Length == 2 && args[0] == Core.Launching.UnelevatedLauncher.HelperOption)
        {
            return Core.Launching.UnelevatedLauncher.RunHelper(args[1]);
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirstInstance)
        {
            SingleInstance.ActivateExistingInstance();
            return 0;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
