namespace AutoClickerMac.Core;

/// <summary>macOS 가상 키코드(ANSI 키보드 기준)와 표시 이름.</summary>
internal static class MacKeys
{
    public const int Escape = 53;
    public const int F6 = 97;
    public const int F8 = 100;

    private static readonly Dictionary<int, string> Names = new()
    {
        [122] = "F1", [120] = "F2", [99] = "F3", [118] = "F4", [96] = "F5", [97] = "F6",
        [98] = "F7", [100] = "F8", [101] = "F9", [109] = "F10", [103] = "F11", [111] = "F12",
        [105] = "F13", [107] = "F14", [113] = "F15",
        [0] = "A", [11] = "B", [8] = "C", [2] = "D", [14] = "E", [3] = "F", [5] = "G", [4] = "H",
        [34] = "I", [38] = "J", [40] = "K", [37] = "L", [46] = "M", [45] = "N", [31] = "O", [35] = "P",
        [12] = "Q", [15] = "R", [1] = "S", [17] = "T", [32] = "U", [9] = "V", [13] = "W", [7] = "X",
        [16] = "Y", [6] = "Z",
        [29] = "0", [18] = "1", [19] = "2", [20] = "3", [21] = "4", [23] = "5", [22] = "6",
        [26] = "7", [28] = "8", [25] = "9",
        [36] = "Return", [48] = "Tab", [49] = "Space", [51] = "Delete", [53] = "Esc",
        [123] = "←", [124] = "→", [125] = "↓", [126] = "↑",
        [27] = "-", [24] = "=", [33] = "[", [30] = "]", [41] = ";", [39] = "'", [43] = ",", [47] = ".",
        [44] = "/", [42] = "\\", [50] = "`",
    };

    public static string Format(int keyCode, MacModifiers modifiers)
    {
        string mods = "";
        if (modifiers.HasFlag(MacModifiers.Control)) mods += "⌃";
        if (modifiers.HasFlag(MacModifiers.Option)) mods += "⌥";
        if (modifiers.HasFlag(MacModifiers.Shift)) mods += "⇧";
        if (modifiers.HasFlag(MacModifiers.Command)) mods += "⌘";
        string key = Names.TryGetValue(keyCode, out var name) ? name : $"키 {keyCode}";
        return mods.Length > 0 ? $"{mods} {key}" : key;
    }
}
