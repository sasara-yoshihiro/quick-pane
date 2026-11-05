namespace QuickPane.Core.Models;

/// <summary>
/// ホットキーの修飾キー (値は RegisterHotKey の MOD_* と同じ)。
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// ホットキー (修飾キー + 仮想キーコード)。
/// </summary>
/// <param name="Modifiers">修飾キー</param>
/// <param name="VirtualKey">仮想キーコード</param>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>既定のホットキー (Ctrl + Space)</summary>
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Ctrl, 0x20);

    private static readonly (HotkeyModifiers Modifier, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Ctrl, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Win, "Win"),
    ];

    private static readonly Dictionary<int, string> KeyNames = CreateKeyNames();

    private static readonly Dictionary<string, int> KeyCodes =
        KeyNames.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>修飾キーと通常キーの両方が指定されているかどうか</summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && IsSupportedKey(VirtualKey);

    /// <summary>
    /// ホットキーに使用できるキーかどうかを判定する。
    /// </summary>
    /// <param name="virtualKey">仮想キーコード</param>
    public static bool IsSupportedKey(int virtualKey) => KeyNames.ContainsKey(virtualKey);

    /// <summary>
    /// 保存用の文字列 (例: "Ctrl+Space") に変換する。
    /// </summary>
    public override string ToString() => string.Join("+", GetParts());

    /// <summary>
    /// 表示用の文字列 (例: "Ctrl + Space") に変換する。
    /// </summary>
    public string ToDisplayString() => string.Join(" + ", GetParts());

    /// <summary>
    /// 修飾キーのみの表示用文字列を作成する (キー入力途中の表示用)。
    /// </summary>
    /// <param name="modifiers">修飾キー</param>
    public static string ToDisplayString(HotkeyModifiers modifiers) =>
        string.Join(" + ", ModifierNames.Where(m => modifiers.HasFlag(m.Modifier)).Select(m => m.Name));

    /// <summary>
    /// 文字列 (例: "Ctrl+Space") からホットキーを解析する。
    /// </summary>
    /// <param name="text">文字列</param>
    /// <param name="gesture">解析結果</param>
    /// <returns>有効なホットキーとして解析できた場合は true</returns>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        var key = 0;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var modifier = ModifierNames.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase));
            if (modifier.Name is not null)
            {
                modifiers |= modifier.Modifier;
            }
            else if (key == 0 && KeyCodes.TryGetValue(part, out var code))
            {
                key = code;
            }
            else
            {
                return false;
            }
        }

        gesture = new HotkeyGesture(modifiers, key);
        return gesture.IsValid;
    }

    private IEnumerable<string> GetParts()
    {
        foreach (var (modifier, name) in ModifierNames)
        {
            if (Modifiers.HasFlag(modifier))
            {
                yield return name;
            }
        }

        yield return KeyNames.TryGetValue(VirtualKey, out var keyName) ? keyName : $"0x{VirtualKey:X2}";
    }

    private static Dictionary<int, string> CreateKeyNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x6A] = "NumMultiply",
            [0x6B] = "NumAdd",
            [0x6D] = "NumSubtract",
            [0x6E] = "NumDecimal",
            [0x6F] = "NumDivide",
            [0xBA] = "Oem1",
            [0xBB] = "OemPlus",
            [0xBC] = "OemComma",
            [0xBD] = "OemMinus",
            [0xBE] = "OemPeriod",
            [0xBF] = "Oem2",
            [0xC0] = "Oem3",
            [0xDB] = "Oem4",
            [0xDC] = "Oem5",
            [0xDD] = "Oem6",
            [0xDE] = "Oem7",
            [0xE2] = "Oem102",
        };

        for (var c = '0'; c <= '9'; c++)
        {
            names[c] = c.ToString();
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }

        for (var i = 0; i <= 9; i++)
        {
            names[0x60 + i] = $"Num{i}";
        }

        for (var i = 1; i <= 24; i++)
        {
            names[0x6F + i] = $"F{i}";
        }

        return names;
    }
}
