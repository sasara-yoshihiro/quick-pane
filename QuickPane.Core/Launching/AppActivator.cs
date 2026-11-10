using System.Runtime.InteropServices;

namespace QuickPane.Core.Launching;

/// <summary>
/// ストアアプリを AppUserModelID で起動する (IApplicationActivationManager)。
/// shell:AppsFolder 経由の起動と異なり、引数を確実に渡せ、失敗した場合のエラーも取得できる。
/// </summary>
internal static class AppActivator
{
    private const int ActivateOptionsNone = 0;

    /// <summary>
    /// ストアアプリを起動する。
    /// </summary>
    /// <param name="appUserModelId">AppUserModelID</param>
    /// <param name="arguments">引数</param>
    /// <returns>起動したプロセスの ID</returns>
    /// <exception cref="COMException">起動に失敗した場合</exception>
    public static uint Activate(string appUserModelId, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            manager.ActivateApplication(appUserModelId, string.IsNullOrEmpty(arguments) ? null : arguments, ActivateOptionsNone, out var processId);
            return processId;
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
        }
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager
    {
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            int options,
            out uint processId);
    }
}
