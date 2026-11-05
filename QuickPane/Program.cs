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
    private static int Main()
    {
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
