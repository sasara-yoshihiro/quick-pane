using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using log4net;
using QuickPane.Core.Launching;
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
    /// アイコンの取得元。
    /// </summary>
    /// <param name="ImageFile">そのまま使用する画像ファイル (指定されている場合は取得しない)</param>
    /// <param name="Key">キャッシュのキー</param>
    /// <param name="Source">取得元 (ログ用)</param>
    /// <param name="Extract">取得処理</param>
    private sealed record IconSource(string? ImageFile, string Key, string Source, Func<IconBitmap?>? Extract);

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
        var source = Describe(path, iconPath, size);
        if (source is null)
        {
            return null;
        }

        return source.ImageFile ?? await GetOrCreateAsync(source.Key, source.Source, source.Extract!);
    }

    /// <summary>
    /// アイコンの画像 (PNG 等) を取得する。キャッシュがあれば使用するが、新たにキャッシュは作成しない
    /// (アプリ一覧・編集ダイアログのプレビュー等、一時的に表示するアイコン用)。
    /// </summary>
    /// <param name="path">アイテムのパス</param>
    /// <param name="iconPath">アイコンの場所 ("ファイル,インデックス" 形式、空の場合はパスから取得)</param>
    /// <param name="size">サイズ (ピクセル)</param>
    /// <returns>画像のデータ、取得できなかった場合は null</returns>
    public async Task<byte[]?> GetIconBytesAsync(string path, string iconPath, int size)
    {
        var source = Describe(path, iconPath, size);
        if (source is null)
        {
            return null;
        }

        var file = source.ImageFile ?? Path.Combine(_cacheDirectory, CreateCacheName(source.Key));
        if (source.ImageFile is not null || File.Exists(file))
        {
            try
            {
                return await Task.Run(() => File.ReadAllBytes(file)).WaitAsync(_timeout).ConfigureAwait(false);
            }
            catch (Exception ex) when (source.ImageFile is not null)
            {
                Log.Warn($"アイコン画像を読み込めませんでした。{file} ({ex.GetType().Name}: {ex.Message})");
                return null;
            }
            catch (Exception)
            {
                // キャッシュを読めない場合は取得し直す
            }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var bitmap = await RunOnStaThreadAsync(source.Extract!, _timeout).ConfigureAwait(false);
            if (bitmap is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            PngWriter.Write(stream, bitmap);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warn($"アイコンを取得できませんでした。{source.Source} ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 登録アイテムで使用していないキャッシュ (削除・変更したアイテム、変更前のアイコンサイズのもの等) を削除する。
    /// </summary>
    /// <param name="items">登録アイテム (パス・アイコンの場所)</param>
    /// <param name="size">現在のアイコンサイズ (ピクセル)</param>
    public void PruneCache(IEnumerable<(string Path, string IconPath)> items, int size)
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return;
        }

        var used = items
            .Select(i => Describe(i.Path, i.IconPath, size))
            .Where(s => s is { ImageFile: null })
            .Select(s => CreateCacheName(s!.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
        {
            var name = Path.GetFileName(file);
            var unused = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? !used.Contains(name)
                : File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1);   // 書き込み途中で残った一時ファイル
            if (!unused)
            {
                continue;
            }

            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                Log.Warn($"アイコンキャッシュを削除できませんでした。{file} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        if (deleted > 0)
        {
            Log.Info($"使用していないアイコンキャッシュを削除しました。{deleted} 件");
        }
    }

    /// <summary>
    /// アイコンの取得元 (キャッシュのキー・取得処理) を決める。
    /// </summary>
    /// <returns>取得元がない (パスが空) 場合は null</returns>
    private IconSource? Describe(string path, string iconPath, int size)
    {
        var expandedPath = Environment.ExpandEnvironmentVariables(path ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var (file, index) = ParseIconLocation(iconPath);
            if (ImageExtensions.Contains(Path.GetExtension(file)))
            {
                // 画像ファイルはそのまま使用する
                return new IconSource(file, string.Empty, file, null);
            }

            return new IconSource(null, $"res|{file}|{index}|{size}", file, () => IconExtractor.FromIconResource(file, index, size));
        }

        if (string.IsNullOrEmpty(expandedPath))
        {
            return null;
        }

        if (PathClassifier.IsUrl(expandedPath) && PathClassifier.Classify(expandedPath) == ItemKind.Url)
        {
            // URL は関連付けられたアプリ (ブラウザ等) のアイコンを使用する
            var scheme = PathClassifier.GetScheme(expandedPath)!;
            return new IconSource(null, $"url|{scheme}|{size}", expandedPath, () =>
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

        return new IconSource(null, $"shell|{expandedPath}|{size}", expandedPath, () => IconExtractor.FromShell(ResolveExecutable(expandedPath), size));
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
            // ストアアプリが更新されて exe のパスが変わった場合は、現在のバージョンの exe から取得する
            // (アプリ一覧のパスはシェルからストアアプリのアイコンを取得できるため、そのまま使う)
            return PackagedApp.IsAppsFolderPath(path) ? path : PackagedApp.ResolveFileSystemPath(path);
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
