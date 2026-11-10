using System.Runtime.InteropServices;
using System.Xml.Linq;
using log4net;

namespace QuickPane.Core.Launching;

/// <summary>
/// ストアアプリ (MSIX パッケージ、C:\Program Files\WindowsApps 配下) と、アプリ一覧 (shell:AppsFolder) のパスの扱い。
/// ストアアプリの exe はパスを指定して直接起動すると、管理者として実行できない・アプリの更新でパス (バージョン) が変わる
/// といった問題があるため、AppUserModelID で起動する。
/// </summary>
public static class PackagedApp
{
    /// <summary>アプリ一覧のパスの接頭辞</summary>
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    private const int ErrorSuccess = 0;

    private const int ErrorInsufficientBuffer = 122;

    private static readonly ILog Log = LogManager.GetLogger(typeof(PackagedApp));

    private sealed record ApplicationEntry(string Id, string? Executable);

    /// <summary>
    /// アプリ一覧のパス (shell:AppsFolder\...) かどうかを判定する。
    /// </summary>
    /// <param name="path">パス</param>
    public static bool IsAppsFolderPath(string? path) =>
        path is not null && path.Trim().StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ストアアプリの AppUserModelID を取得する。
    /// </summary>
    /// <param name="path">
    /// ストアアプリの exe のパス (例: C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.25.2733.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe)
    /// またはアプリ一覧のパス (例: shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App)
    /// </param>
    /// <param name="appUserModelId">AppUserModelID (例: Microsoft.WindowsTerminal_8wekyb3d8bbwe!App)</param>
    /// <returns>ストアアプリとして取得できた場合は true</returns>
    public static bool TryGetAppUserModelId(string path, out string appUserModelId)
    {
        appUserModelId = string.Empty;
        if (IsAppsFolderPath(path))
        {
            // ストアアプリは「ファミリ名!アプリ ID」、デスクトップアプリは「{フォルダ ID}\exe のパス」等
            var name = path.Trim()[AppsFolderPrefix.Length..];
            if (name.Contains('!') && !name.Contains('\\'))
            {
                appUserModelId = name;
                return true;
            }

            return false;
        }

        if (!TrySplitPath(path, out var windowsApps, out var packageFullName, out var relativePath)
            || GetFamilyName(packageFullName) is not { } familyName)
        {
            return false;
        }

        try
        {
            // アプリの更新でバージョンが変わっている場合は、インストールされている現在のバージョンのフォルダを使う
            var packageDirectory = Path.Combine(windowsApps, packageFullName);
            if (!File.Exists(Path.Combine(packageDirectory, "AppxManifest.xml")))
            {
                packageDirectory = FindInstalledPackagePath(familyName);
            }

            var exeExists = File.Exists(Path.Combine(windowsApps, packageFullName, relativePath));
            if (packageDirectory is null || FindApplication(packageDirectory, relativePath, exeExists) is not { } application)
            {
                return false;
            }

            appUserModelId = $"{familyName}!{application.Id}";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"ストアアプリの情報を取得できませんでした。パス=\"{path}\"", ex);
            return false;
        }
    }

    /// <summary>
    /// アイコンの取得・ファイルの場所を開く処理用に、実際に存在するファイルのパスを取得する。
    /// ストアアプリが更新されて exe のパスが変わった場合は現在のバージョンのパス、アプリ一覧のパスの場合は exe のパスを返す。
    /// </summary>
    /// <param name="path">パス</param>
    /// <returns>実際のパス (変換が不要・取得できない場合は元のパス)</returns>
    public static string ResolveFileSystemPath(string path)
    {
        try
        {
            if (IsAppsFolderPath(path))
            {
                return ResolveAppsFolderPath(path.Trim()[AppsFolderPrefix.Length..]) ?? path;
            }

            if (File.Exists(path)
                || !TrySplitPath(path, out _, out var packageFullName, out var relativePath)
                || GetFamilyName(packageFullName) is not { } familyName
                || FindInstalledPackagePath(familyName) is not { } packageDirectory)
            {
                return path;
            }

            var candidate = Path.Combine(packageDirectory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            return FindApplication(packageDirectory, relativePath, exeExists: false) is { Executable: { } executable }
                ? Path.Combine(packageDirectory, executable)
                : path;
        }
        catch (Exception ex)
        {
            Log.Warn($"アプリのパスを取得できませんでした。パス=\"{path}\"", ex);
            return path;
        }
    }

    /// <summary>
    /// アプリ一覧の名前 (AppUserModelID または「{フォルダ ID}\相対パス」) から exe 等のパスを取得する。
    /// </summary>
    private static string? ResolveAppsFolderPath(string name)
    {
        if (name.Contains('!') && !name.Contains('\\'))
        {
            var separator = name.IndexOf('!');
            var familyName = name[..separator];
            var applicationId = name[(separator + 1)..];
            if (FindInstalledPackagePath(familyName) is not { } packageDirectory)
            {
                return null;
            }

            var application = ReadApplications(packageDirectory)
                .FirstOrDefault(a => string.Equals(a.Id, applicationId, StringComparison.OrdinalIgnoreCase));
            return application?.Executable is { } executable ? Path.Combine(packageDirectory, executable) : null;
        }

        // {6D809377-6AF0-444B-8957-A3773F02200E}\Microsoft VS Code\Code.exe のような既知フォルダ ID 付きのパス
        if (name.StartsWith('{') && name.IndexOf('}') is var end and > 0 && Guid.TryParse(name[..(end + 1)], out var folderId))
        {
            var folder = GetKnownFolderPath(folderId);
            return folder is null ? null : Path.Combine(folder, name[(end + 1)..].TrimStart('\\'));
        }

        return Path.IsPathFullyQualified(name) ? name : null;
    }

    /// <summary>
    /// WindowsApps フォルダ配下のパスを、WindowsApps フォルダ・パッケージ完全名・パッケージ内の相対パスに分ける。
    /// </summary>
    private static bool TrySplitPath(string path, out string windowsApps, out string packageFullName, out string relativePath)
    {
        windowsApps = string.Empty;
        packageFullName = string.Empty;
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            return false;
        }

        // 区切り文字は \ と / のどちらも受け付ける
        var segments = path.Trim().Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(segments, s => string.Equals(s, "WindowsApps", StringComparison.OrdinalIgnoreCase));
        if (index < 1 || index + 2 >= segments.Length)
        {
            return false;
        }

        windowsApps = string.Join('\\', segments[..(index + 1)]);
        packageFullName = segments[index + 1];
        relativePath = string.Join('\\', segments[(index + 2)..]);
        return true;
    }

    /// <summary>
    /// パッケージ完全名 (名前_バージョン_アーキテクチャ_リソース ID_発行元 ID) からパッケージ ファミリ名 (名前_発行元 ID) を取得する。
    /// </summary>
    private static string? GetFamilyName(string packageFullName)
    {
        var parts = packageFullName.Split('_');
        return parts.Length >= 5 ? $"{parts[0]}_{parts[^1]}" : null;
    }

    private static List<ApplicationEntry> ReadApplications(string packageDirectory) =>
        XDocument.Load(Path.Combine(packageDirectory, "AppxManifest.xml")).Descendants()
            .Where(e => e.Name.LocalName == "Application" && !string.IsNullOrEmpty((string?)e.Attribute("Id")))
            .Select(e => new ApplicationEntry((string)e.Attribute("Id")!, ((string?)e.Attribute("Executable"))?.Replace('/', '\\')))
            .ToList();

    /// <summary>
    /// マニフェストから、exe に対応するアプリを取得する。
    /// </summary>
    private static ApplicationEntry? FindApplication(string packageDirectory, string relativePath, bool exeExists)
    {
        var applications = ReadApplications(packageDirectory);
        var match = applications.FirstOrDefault(a => string.Equals(a.Executable, relativePath, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        // exe 名だけ一致する場合 (更新でフォルダ構成が変わった場合)
        var fileName = Path.GetFileName(relativePath);
        match = applications.FirstOrDefault(a => string.Equals(Path.GetFileName(a.Executable), fileName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        // アプリが 1 つだけの場合、または更新で exe がなくなった場合は、パッケージのメインのアプリ (先頭) とする
        // (exe が存在するのにアプリとして宣言されていない場合は、補助的な exe のため直接起動する)
        return applications.Count == 1 || (!exeExists && applications.Count > 0) ? applications[0] : null;
    }

    /// <summary>
    /// パッケージ ファミリ名から、現在のユーザーにインストールされているパッケージのフォルダを取得する。
    /// </summary>
    private static string? FindInstalledPackagePath(string familyName)
    {
        uint count = 0;
        uint bufferLength = 0;
        var result = GetPackagesByPackageFamily(familyName, ref count, null, ref bufferLength, null);
        if (result != ErrorInsufficientBuffer || count == 0)
        {
            return null;
        }

        var fullNames = new IntPtr[count];
        var buffer = new char[bufferLength];
        if (GetPackagesByPackageFamily(familyName, ref count, fullNames, ref bufferLength, buffer) != ErrorSuccess)
        {
            return null;
        }

        var fullName = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (fullName is null)
        {
            return null;
        }

        uint pathLength = 0;
        if (GetPackagePathByFullName(fullName, ref pathLength, null) != ErrorInsufficientBuffer)
        {
            return null;
        }

        var path = new char[pathLength];
        return GetPackagePathByFullName(fullName, ref pathLength, path) == ErrorSuccess
            ? new string(path, 0, (int)pathLength).TrimEnd('\0')
            : null;
    }

    private static string? GetKnownFolderPath(Guid folderId)
    {
        if (SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, IntPtr[]? packageFullNames, ref uint bufferLength, char[]? buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, char[]? path);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
