using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using log4net;

namespace QuickPane.Core.Storage;

/// <summary>データファイルの読み込み結果</summary>
public enum LoadStatus
{
    /// <summary>正常に読み込んだ</summary>
    Loaded,

    /// <summary>ファイルが存在しないため初期状態を作成した</summary>
    Created,

    /// <summary>ファイルが破損していたため .bak から復元した</summary>
    RestoredFromBackup,

    /// <summary>ファイル・.bak ともに読み込めなかったため、破損ファイルを退避して初期状態を作成した</summary>
    Reset,
}

/// <summary>
/// データファイルの読み込み結果。
/// </summary>
/// <typeparam name="T">データの型</typeparam>
/// <param name="Value">読み込んだデータ</param>
/// <param name="Status">読み込み結果</param>
/// <param name="CorruptFilePath">破損ファイルの退避先 (Reset の場合のみ)</param>
public sealed record LoadResult<T>(T Value, LoadStatus Status, string? CorruptFilePath = null);

/// <summary>
/// JSON データファイルの読み書き。
/// 保存時は一時ファイルに書き込んでから置き換え、置き換え前のファイルを .bak として 1 世代保持する。
/// </summary>
public static class JsonFileStore
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(JsonFileStore));

    private static readonly object SyncRoot = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// .bak ファイルのパスを取得する。
    /// </summary>
    /// <param name="path">データファイルのパス</param>
    public static string GetBackupPath(string path) => path + ".bak";

    /// <summary>
    /// データファイルを読み込む。破損している場合は .bak からの復元を試み、復元できない場合は初期状態を返す。
    /// </summary>
    /// <typeparam name="T">データの型</typeparam>
    /// <param name="path">データファイルのパス</param>
    /// <param name="createDefault">初期状態を作成する処理</param>
    /// <param name="normalize">読み込み後の補正処理</param>
    public static LoadResult<T> Load<T>(string path, Func<T> createDefault, Action<T> normalize)
        where T : class
    {
        lock (SyncRoot)
        {
            var backupPath = GetBackupPath(path);
            if (!File.Exists(path) && !File.Exists(backupPath))
            {
                var created = createDefault();
                TrySave(path, created);
                return new LoadResult<T>(created, LoadStatus.Created);
            }

            if (TryRead(path, out T? value))
            {
                normalize(value!);
                return new LoadResult<T>(value!, LoadStatus.Loaded);
            }

            if (TryRead(backupPath, out T? backup))
            {
                Log.Warn($"データファイルを読み込めないため、バックアップから復元しました。{path}");
                normalize(backup!);
                MoveCorruptFile(path);
                TrySave(path, backup!);
                return new LoadResult<T>(backup!, LoadStatus.RestoredFromBackup);
            }

            var corruptPath = MoveCorruptFile(path);
            Log.Error($"データファイル・バックアップともに読み込めないため、初期状態で起動します。退避先={corruptPath}");
            var reset = createDefault();
            TrySave(path, reset);
            return new LoadResult<T>(reset, LoadStatus.Reset, corruptPath);
        }
    }

    /// <summary>
    /// データファイルを保存する。
    /// </summary>
    /// <typeparam name="T">データの型</typeparam>
    /// <param name="path">データファイルのパス</param>
    /// <param name="value">保存するデータ</param>
    /// <exception cref="IOException">保存に失敗した場合</exception>
    public static void Save<T>(string path, T value)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var tempPath = path + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                // 置き換えと同時に旧ファイルを .bak として保持する
                File.Replace(tempPath, path, GetBackupPath(path), ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
    }

    /// <summary>
    /// データファイルを保存する。失敗した場合はログを出力して false を返す。
    /// </summary>
    /// <typeparam name="T">データの型</typeparam>
    /// <param name="path">データファイルのパス</param>
    /// <param name="value">保存するデータ</param>
    public static bool TrySave<T>(string path, T value)
    {
        try
        {
            Save(path, value);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"データファイルの保存に失敗しました。{path}", ex);
            return false;
        }
    }

    private static bool TryRead<T>(string path, out T? value)
        where T : class
    {
        value = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonOptions);
            return value is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Warn($"データファイルを読み込めませんでした。{path}", ex);
            return false;
        }
    }

    private static string? MoveCorruptFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var corruptPath = $"{path}.corrupt_{DateTime.Now:yyyyMMdd_HHmmss}";
        try
        {
            File.Move(path, corruptPath, overwrite: true);
            return corruptPath;
        }
        catch (Exception ex)
        {
            Log.Error($"破損したデータファイルを退避できませんでした。{path}", ex);
            return null;
        }
    }
}
