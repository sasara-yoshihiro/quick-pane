using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace QuickPane.Core.Icons;

/// <summary>
/// シェル・アイコンリソースからアイコン画像を取得する (Win32)。
/// 時間がかかる場合があるため、UI スレッド以外の STA スレッドから呼び出すこと。
/// </summary>
internal static unsafe class IconExtractor
{
    private const int EPending = unchecked((int)0x8000000A);

    private static readonly HashSet<string> IconResourceExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".ico" };

    /// <summary>
    /// シェル経由でパスのアイコンを取得する。
    /// </summary>
    /// <param name="path">ファイル・フォルダのパス</param>
    /// <param name="size">サイズ (ピクセル)</param>
    public static IconBitmap? FromShell(string path, int size)
    {
        // シェルのアイコンキャッシュが未作成の場合は E_PENDING が返るため、少し待って再試行する
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return FromShellCore(path, size);
            }
            catch (COMException ex) when (ex.HResult == EPending && attempt < 3)
            {
                Thread.Sleep(200);
            }
            catch (COMException ex) when (ex.HResult == EPending && HasIconResource(path))
            {
                return FromIconResource(path, 0, size);
            }
        }
    }

    private static IconBitmap? FromShellCore(string path, int size)
    {
        object instance;
        var iid = typeof(IShellItemImageFactory).GUID;
        fixed (char* p = path)
        {
            PInvoke.SHCreateItemFromParsingName(new PCWSTR(p), null, &iid, out instance).ThrowOnFailure();
        }

        try
        {
            var factory = (IShellItemImageFactory)instance;
            HBITMAP bitmap;
            factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY, &bitmap);
            try
            {
                return ReadBitmap(bitmap, ensureStraightAlpha: true);
            }
            finally
            {
                PInvoke.DeleteObject(new HGDIOBJ(bitmap.Value));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    /// <summary>
    /// アイコンリソース (.ico / .exe / .dll 等) からアイコンを取得する。
    /// </summary>
    /// <param name="file">ファイルのパス</param>
    /// <param name="index">アイコンのインデックス (負の値の場合はリソース ID)</param>
    /// <param name="size">サイズ (ピクセル)</param>
    public static IconBitmap? FromIconResource(string file, int index, int size)
    {
        HICON icon = default;
        uint iconId = 0;
        uint count;
        fixed (char* p = file)
        {
            count = PInvoke.PrivateExtractIcons(new PCWSTR(p), index, size, size, &icon, &iconId, 1, 0);
        }

        if (count == 0 || count == uint.MaxValue || icon.IsNull)
        {
            return null;
        }

        try
        {
            return ReadIcon(icon);
        }
        finally
        {
            PInvoke.DestroyIcon(icon);
        }
    }

    /// <summary>
    /// URL スキーム (http 等) に関連付けられたアプリのアイコンの場所を取得する。
    /// </summary>
    /// <param name="scheme">スキーム</param>
    /// <returns>"ファイル,インデックス" 形式、取得できない場合は null</returns>
    public static string? GetSchemeIconLocation(string scheme)
    {
        uint length = 1024;
        var buffer = new char[length];
        var hr = PInvoke.AssocQueryString(ASSOCF.ASSOCF_INIT_IGNOREUNKNOWN, ASSOCSTR.ASSOCSTR_DEFAULTICON, scheme, null, buffer.AsSpan(), ref length);
        if (hr.Failed)
        {
            return null;
        }

        var location = new string(buffer, 0, Math.Max(0, (int)length - 1)).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    private static bool HasIconResource(string path) => IconResourceExtensions.Contains(Path.GetExtension(path));

    private static IconBitmap? ReadIcon(HICON icon)
    {
        ICONINFO info;
        if (!PInvoke.GetIconInfo(icon, &info))
        {
            return null;
        }

        try
        {
            if (info.hbmColor.IsNull)
            {
                return null;
            }

            var color = ReadBitmap(info.hbmColor, ensureStraightAlpha: false);
            if (color is null)
            {
                return null;
            }

            // アルファチャネルを持たないアイコンはマスクから透過を作成する
            if (!HasAlpha(color.Bgra) && !info.hbmMask.IsNull)
            {
                var mask = ReadBitmap(info.hbmMask, ensureStraightAlpha: false);
                for (var i = 0; i < color.Bgra.Length; i += 4)
                {
                    var transparent = mask is not null && i + 2 < mask.Bgra.Length && mask.Bgra[i] != 0;
                    color.Bgra[i + 3] = transparent ? (byte)0 : (byte)255;
                }
            }

            return color;
        }
        finally
        {
            if (!info.hbmColor.IsNull)
            {
                PInvoke.DeleteObject(new HGDIOBJ(info.hbmColor.Value));
            }

            if (!info.hbmMask.IsNull)
            {
                PInvoke.DeleteObject(new HGDIOBJ(info.hbmMask.Value));
            }
        }
    }

    private static IconBitmap? ReadBitmap(HBITMAP bitmap, bool ensureStraightAlpha)
    {
        BITMAP info;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &info) == 0)
        {
            return null;
        }

        var width = info.bmWidth;
        var height = Math.Abs(info.bmHeight);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var header = new BITMAPINFO();
        header.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        header.bmiHeader.biWidth = width;
        header.bmiHeader.biHeight = -height; // トップダウン
        header.bmiHeader.biPlanes = 1;
        header.bmiHeader.biBitCount = 32;
        header.bmiHeader.biCompression = 0; // BI_RGB

        var pixels = new byte[width * height * 4];
        var dc = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* p = pixels)
            {
                if (PInvoke.GetDIBits(dc, bitmap, 0, (uint)height, p, &header, DIB_USAGE.DIB_RGB_COLORS) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, dc);
        }

        if (ensureStraightAlpha)
        {
            Unpremultiply(pixels);
        }

        return new IconBitmap(width, height, pixels);
    }

    private static bool HasAlpha(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 乗算済みアルファを非乗算に変換する。
    /// 色成分がアルファ値を超えるピクセルがある場合は、すでに非乗算とみなして変換しない。
    /// </summary>
    private static void Unpremultiply(byte[] bgra)
    {
        if (!HasAlpha(bgra))
        {
            // アルファ情報がない画像は不透明として扱う
            for (var i = 3; i < bgra.Length; i += 4)
            {
                bgra[i] = 255;
            }

            return;
        }

        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
            {
                return;
            }
        }

        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a is 0 or 255)
            {
                continue;
            }

            bgra[i] = (byte)(bgra[i] * 255 / a);
            bgra[i + 1] = (byte)(bgra[i + 1] * 255 / a);
            bgra[i + 2] = (byte)(bgra[i + 2] * 255 / a);
        }
    }
}
