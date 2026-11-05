using Windows.Win32;

namespace QuickPane.Helpers;

/// <summary>
/// 常駐時のメモリ使用量を抑えるための処理。
/// </summary>
internal static class MemoryHelper
{
    /// <summary>
    /// 不要なメモリを解放し、ワーキングセットを縮小する (ランチャー非表示中に呼び出す)。
    /// </summary>
    public static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: true);
        PInvoke.SetProcessWorkingSetSize(PInvoke.GetCurrentProcess(), nuint.MaxValue, nuint.MaxValue);
    }
}
