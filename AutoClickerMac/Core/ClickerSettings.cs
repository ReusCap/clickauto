namespace AutoClickerMac.Core;

internal enum MouseButtonKind { Left, Right, Middle }

internal enum ClickKind { Single, Double }

internal enum PositionMode { Cursor, Fixed }

internal enum TriggerMode { Toggle, Hold }

[Flags]
internal enum MacModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Option = 4,
    Command = 8,
}

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

    /// <summary>macOS 가상 키코드. 기본 97 = F6.</summary>
    public int HotkeyKeyCode { get; set; } = MacKeys.F6;
    public MacModifiers HotkeyModifiers { get; set; }
    public bool AlwaysOnTop { get; set; }

    public ClickerSettings Clone() => (ClickerSettings)MemberwiseClone();

    /// <summary>파일에서 읽은 값이 범위를 벗어나도 안전하게 동작하도록 보정한다.</summary>
    public void Normalize()
    {
        IntervalMs = Math.Clamp(IntervalMs, MinIntervalMs, MaxIntervalMs);
        JitterMs = Math.Clamp(JitterMs, 0, 1000);
        RepeatCount = Math.Max(0, RepeatCount);
        MaxDurationSec = Math.Clamp(MaxDurationSec, 0, 86_400);
        HotkeyModifiers &= MacModifiers.Shift | MacModifiers.Control | MacModifiers.Option | MacModifiers.Command;
        if (HotkeyKeyCode is < 0 or > 127 || HotkeyKeyCode == MacKeys.Escape)
            HotkeyKeyCode = MacKeys.F6;
    }
}
