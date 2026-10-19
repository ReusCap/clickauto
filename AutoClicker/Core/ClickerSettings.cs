namespace AutoClicker.Core;

internal enum MouseButtonKind { Left, Right, Middle }

internal enum ClickKind { Single, Double }

internal enum PositionMode { Cursor, Fixed }

internal enum TriggerMode { Toggle, Hold }

internal sealed class ClickerSettings
{
    public const int MinIntervalMs = 1;
    public const int MaxIntervalMs = 60_000;

    public int IntervalMs { get; set; } = 10;
    public int JitterMs { get; set; }
    public MouseButtonKind Button { get; set; } = MouseButtonKind.Left;
    public ClickKind Click { get; set; } = ClickKind.Single;
    public PositionMode Position { get; set; } = PositionMode.Cursor;
    public int FixedX { get; set; }
    public int FixedY { get; set; }

    /// <summary>0 = 무한 반복.</summary>
    public long RepeatCount { get; set; }

    /// <summary>0 = 제한 없음.</summary>
    public int MaxDurationSec { get; set; }

    public TriggerMode Trigger { get; set; } = TriggerMode.Toggle;
    public Keys HotkeyKey { get; set; } = Keys.F6;
    public Keys HotkeyModifiers { get; set; } = Keys.None;
    public bool AlwaysOnTop { get; set; } = true;

    public ClickerSettings Clone() => (ClickerSettings)MemberwiseClone();

    /// <summary>파일에서 읽은 값이 범위를 벗어나도 안전하게 동작하도록 보정한다.</summary>
    public void Normalize()
    {
        IntervalMs = Math.Clamp(IntervalMs, MinIntervalMs, MaxIntervalMs);
        JitterMs = Math.Clamp(JitterMs, 0, 1000);
        RepeatCount = Math.Max(0, RepeatCount);
        MaxDurationSec = Math.Clamp(MaxDurationSec, 0, 86_400);
        HotkeyModifiers &= Keys.Control | Keys.Alt | Keys.Shift;
        if (HotkeyKey is Keys.None or Keys.Escape)
            HotkeyKey = Keys.F6;
    }

    public static string FormatHotkey(Keys key, Keys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}
