using System.Reflection;
using System.Text.Json;
using log4net;
using log4net.Config;

namespace QuickPane.Core.Configuration;

/// <summary>
/// appsettings.json の内容。
/// </summary>
public sealed class AppSettings
{
    /// <summary>ログ設定</summary>
    public LoggingSettings Logging { get; set; } = new();
}

/// <summary>
/// ログ設定。
/// </summary>
public sealed class LoggingSettings
{
    /// <summary>ログを出力するかどうか</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 設定ファイル (Config/) の読み込みとログ初期化。
/// </summary>
public static class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// appsettings.json を読み込む。存在しない・読み込めない場合は既定値を返す。
    /// </summary>
    public static AppSettings LoadSettings()
    {
        var path = Path.Combine(AppPaths.ConfigDirectory, "appsettings.json");
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // 設定ファイルが壊れていても既定値で動作させる
        }

        return new AppSettings();
    }

    /// <summary>
    /// 設定ファイルを読み込み、設定に従って log4net を初期化する。
    /// </summary>
    public static void ConfigureLogging()
    {
        var settings = LoadSettings();
        if (!settings.Logging.Enabled)
        {
            return;
        }

        var configFile = new FileInfo(Path.Combine(AppPaths.ConfigDirectory, "log4net.config"));
        if (!configFile.Exists)
        {
            return;
        }

        // log4net の相対パスは実行ファイルのフォルダ基準で解決される
        var repository = LogManager.GetRepository(Assembly.GetEntryAssembly() ?? typeof(AppConfig).Assembly);
        XmlConfigurator.Configure(repository, configFile);
    }
}
