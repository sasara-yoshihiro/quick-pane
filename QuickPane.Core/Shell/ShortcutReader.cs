using System.Runtime.InteropServices;
using log4net;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace QuickPane.Core.Shell;

/// <summary>
/// ショートカット (.lnk) の内容。
/// </summary>
/// <param name="TargetPath">リンク先</param>
/// <param name="Arguments">引数</param>
/// <param name="WorkingDirectory">作業フォルダ</param>
/// <param name="IconLocation">アイコンの場所 ("ファイル,インデックス" 形式、未設定の場合は空)</param>
public sealed record ShortcutInfo(string TargetPath, string Arguments, string WorkingDirectory, string IconLocation);

/// <summary>
/// ショートカット (.lnk) の読み取り。
/// </summary>
public static class ShortcutReader
{
    private const int MaxPath = 32768;

    private static readonly ILog Log = LogManager.GetLogger(typeof(ShortcutReader));

    /// <summary>
    /// ショートカットを読み取る。
    /// </summary>
    /// <param name="lnkPath">.lnk ファイルのパス</param>
    /// <returns>読み取れなかった場合は null</returns>
    public static unsafe ShortcutInfo? Read(string lnkPath)
    {
        object? instance = null;
        try
        {
            instance = new ShellLink();
            var link = (IShellLinkW)instance;
            ((IPersistFile)instance).Load(lnkPath, STGM.STGM_READ);

            var buffer = new char[MaxPath];
            fixed (char* p = buffer)
            {
                link.GetPath(new PWSTR(p), buffer.Length, null, (uint)SLGP_FLAGS.SLGP_RAWPATH);
                var target = new string(p);

                link.GetArguments(new PWSTR(p), buffer.Length);
                var arguments = new string(p);

                link.GetWorkingDirectory(new PWSTR(p), buffer.Length);
                var workingDirectory = new string(p);

                link.GetIconLocation(new PWSTR(p), buffer.Length, out var iconIndex);
                var iconFile = new string(p);
                var iconLocation = string.IsNullOrEmpty(iconFile) ? string.Empty : $"{iconFile},{iconIndex}";

                return new ShortcutInfo(target, arguments, workingDirectory, iconLocation);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"ショートカットを読み取れませんでした。{lnkPath}", ex);
            return null;
        }
        finally
        {
            if (instance is not null)
            {
                Marshal.ReleaseComObject(instance);
            }
        }
    }
}
