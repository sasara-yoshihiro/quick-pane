using System.Globalization;
using System.Runtime.InteropServices;
using log4net;
using QuickPane.Core.Launching;

namespace QuickPane.Core.Shell;

/// <summary>
/// インストール済みアプリ。
/// </summary>
/// <param name="Name">表示名</param>
/// <param name="Path">起動用のパス (shell:AppsFolder\...)</param>
public sealed record InstalledApp(string Name, string Path);

/// <summary>
/// インストール済みアプリの一覧 (スタートメニューの「すべてのアプリ」と同じ shell:AppsFolder の内容) を取得する。
/// ストアアプリも含まれ、WindowsApps フォルダの exe のパスを指定しなくても登録できる。
/// </summary>
public static class InstalledApps
{
    private const uint SigdnNormalDisplay = 0;

    private const uint SigdnParentRelativeParsing = 0x80018001;

    private static readonly Guid FolderIdAppsFolder = new("1E87508D-89C2-42F0-8A7E-645A0F50CA58");

    private static readonly Guid BhidEnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");

    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private static readonly Guid IidEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");

    private static readonly ILog Log = LogManager.GetLogger(typeof(InstalledApps));

    /// <summary>
    /// インストール済みアプリの一覧を取得する (名前順)。
    /// </summary>
    public static Task<IReadOnlyList<InstalledApp>> GetAsync()
    {
        // シェルの COM オブジェクトは STA で扱う
        var completion = new TaskCompletionSource<IReadOnlyList<InstalledApp>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Enumerate());
            }
            catch (Exception ex)
            {
                Log.Error("インストール済みアプリの一覧を取得できませんでした。", ex);
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "InstalledApps",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static List<InstalledApp> Enumerate()
    {
        var folderId = FolderIdAppsFolder;
        var iidShellItem = IidShellItem;
        Marshal.ThrowExceptionForHR(SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref iidShellItem, out var folder));

        var bhid = BhidEnumItems;
        var iidEnum = IidEnumShellItems;
        var enumerator = (IEnumShellItems)((IShellItem)folder).BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum);

        var apps = new List<InstalledApp>();
        try
        {
            while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    var name = GetDisplayName(item, SigdnNormalDisplay);
                    var parsingName = GetDisplayName(item, SigdnParentRelativeParsing);
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parsingName))
                    {
                        apps.Add(new InstalledApp(name, PackagedApp.AppsFolderPrefix + parsingName));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(folder);
        }

        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType);
        return apps.OrderBy(a => a.Name, comparer).ToList();
    }

    private static string GetDisplayName(IShellItem item, uint type)
    {
        var pointer = item.GetDisplayName(type);
        try
        {
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderItem(ref Guid folderId, uint flags, IntPtr token, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid);

        void GetParent();

        IntPtr GetDisplayName(uint type);
    }

    [ComImport]
    [Guid("70629033-E363-4A28-A567-0DB78006E6D7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint count, out IShellItem item, out uint fetched);
    }
}
