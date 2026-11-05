using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using log4net;
using QuickPane.Core.Models;

namespace QuickPane.Core.Icons;

/// <summary>
/// アイテムのアイコン取得とキャッシュ。
/// 取得はバックグラウンド (STA スレッド) で行い、タイムアウトした場合・失敗した場合は null を返す。
/// </summary>
public sealed class IconService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(IconService));

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    private readonly string _cacheDirectory;

    private readonly TimeSpan _timeout;

    private readonly SemaphoreSlim _gate = new(3);

    private readonly Dictionary<string, Task<string?>> _pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="cacheDirectory">キャッシュフォルダ</param>
    /// <param name="timeout">1 件あたりのタイムアウト</param>
    public IconService(string cacheDirectory, TimeSpan timeout)
    {
        _cacheDirectory = cacheDirectory;
        _timeout = timeout;
    }

    /// <summary>
    /// アイコンの画像ファイル (PNG 等) のパスを取得する。キャッシュがない場合は取得してキャッシュする。
    /// </summary>
    /// <param name="item">アイテム</param>
    /// <param name="size">サイズ (ピクセル)</param>
    /// <returns>画像ファイルのパス、取得できなかった場合は null</returns>
    public Task<string?> GetIconFileAsync(LauncherItem item, int size) =>
        GetIconFileAsync(item.Path, item.IconPath, size);

    /// <summary>
    /// アイコンの画像ファイル (PNG 等) のパスを取得する。キャッシュがない場合は取得してキャッシュする。
    /// </summary>
    /// <param name="path">アイテムのパス</param>
    /// <param name="iconPath">アイコンの場所 ("ファイル,インデックス" 形式、空の場合はパスから取得)</param>
    /// <param name="size">サイズ (ピクセル)</param>
    /// <returns>画像ファイルのパス、取得できなかった場合は null</returns>
    public async Task<string?> GetIconFileAsync(string path, string iconPath, int size)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(path ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var (file, index) = ParseIconLocation(iconPath);
            if (ImageExtensions.Contains(Path.GetExtension(file)))
            {
                // 画像ファイルはそのまま使用する
                return file;
            }

            return await GetOrCreateAsync($"res|{file}|{index}|{size}", file, () => IconExtractor.FromIconResource(file, index, size));
        }

        if (string.IsNullOrEmpty(expandedPath))
        {
            return null;
        }

        if (PathClassifier.IsUrl(expandedPath) && PathClassifier.Classify(expandedPath) == ItemKind.Url)
        {
            // URL は関連付けられたアプリ (ブラウザ等) のアイコンを使用する
            var scheme = PathClassifier.GetScheme(expandedPath)!;
            return await GetOrCreateAsync($"url|{scheme}|{size}", expandedPath, () =>
            {
                var location = IconExtractor.GetSchemeIconLocation(scheme);
                if (location is null)
                {
                    return null;
                }

                var (file, index) = ParseIconLocation(location);
                return IconExtractor.FromIconResource(file, index, size);
            });
        }

        return await GetOrCreateAsync($"shell|{expandedPath}|{size}", expandedPath, () => IconExtractor.FromShell(ResolveExecutable(expandedPath), size));
    }

    /// <summary>
    /// キャッシュをすべて削除する。
    /// </summary>
    public void ClearCache()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.Warn($"アイコンキャッシュを削除できませんでした。{file}", ex);
            }
        }

        Log.Info("アイコンキャッシュを削除しました。");
    }

    /// <summary>
    /// "ファイル,インデックス" 形式のアイコンの場所を分解する。
    /// </summary>
    /// <param name="location">アイコンの場所</param>
    public static (string File, int Index) ParseIconLocation(string location)
    {
        var text = location.Trim().Trim('"');
        var comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return (Environment.ExpandEnvironmentVariables(text[..comma].Trim().Trim('"')), index);
        }

        return (Environment.ExpandEnvironmentVariables(text), 0);
    }

    private Task<string?> GetOrCreateAsync(string key, string source, Func<IconBitmap?> extract)
    {
        var cacheFile = Path.Combine(_cacheDirectory, CreateCacheName(key));
        if (File.Exists(cacheFile))
        {
            return Task.FromResult<string?>(cacheFile);
        }

        // 同じキャッシュを使用するアイテム (同じスキームの URL 等) の取得は 1 回にまとめる
        lock (_pending)
        {
            if (_pending.TryGetValue(cacheFile, out var running))
            {
                return running;
            }

            var task = CreateAsync(cacheFile, source, extract);
            _pending[cacheFile] = task;
            _ = task.ContinueWith(
                _ =>
                {
                    lock (_pending)
                    {
                        _pending.Remove(cacheFile);
                    }
                },
                TaskScheduler.Default);
            return task;
        }
    }

    private async Task<string?> CreateAsync(string cacheFile, string source, Func<IconBitmap?> extract)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(cacheFile))
            {
                return cacheFile;
            }

            var bitmap = await RunOnStaThreadAsync(extract, _timeout).ConfigureAwait(false);
            if (bitmap is null)
            {
                Log.Warn($"アイコンを取得できませんでした。{source}");
                return null;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var tempFile = $"{cacheFile}.{Guid.NewGuid():N}.tmp";
            using (var stream = File.Create(tempFile))
            {
                PngWriter.Write(stream, bitmap);
            }

            File.Move(tempFile, cacheFile, overwrite: true);
            return cacheFile;
        }
        catch (TimeoutException)
        {
            Log.Warn($"アイコンの取得がタイムアウトしました。{source}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"アイコンを取得できませんでした。{source} ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// フォルダを含まない実行ファイル名 (例: mspaint.exe) を、App Paths レジストリ・PATH 環境変数からフルパスに解決する。
    /// </summary>
    private static string ResolveExecutable(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains('/'))
        {
            return path;
        }

        var fileName = Path.HasExtension(path) ? path : path + ".exe";
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{fileName}");
            if (key?.GetValue(null) is string registered && !string.IsNullOrWhiteSpace(registered))
            {
                return Environment.ExpandEnvironmentVariables(registered.Trim('"'));
            }
        }

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim()), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }

    private static string CreateCacheName(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant() + ".png";
    }

    /// <summary>
    /// シェルの COM 呼び出し用に STA スレッドで処理を実行する。
    /// ネットワーク上のパス等で応答がない場合は、スレッドを放棄して TimeoutException を送出する。
    /// </summary>
    private static async Task<T?> RunOnStaThreadAsync<T>(Func<T?> func, TimeSpan timeout)
        where T : class
    {
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(func());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "QuickPane.IconExtractor",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != completion.Task)
        {
            throw new TimeoutException();
        }

        return await completion.Task.ConfigureAwait(false);
    }
}
