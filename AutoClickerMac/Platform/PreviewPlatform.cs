using AutoClickerMac.Core;

namespace AutoClickerMac.Platform;

/// <summary>
/// 맥이 아닌 곳(Windows 개발 PC)에서 화면만 확인하기 위한 가짜 구현.
/// 실제로 클릭하지 않고 횟수만 센다. 단축키는 동작하지 않는다.
/// </summary>
internal sealed class PreviewPlatform : IPlatform
{
    private readonly PreviewHotkeys _hotkeys = new();

    public bool IsPreview => true;
    public IHotkeySource Hotkeys => _hotkeys;

    public bool IsAccessibilityTrusted() => true;

    public void OpenAccessibilitySettings() { }

    public bool Click(MouseButtonKind button, bool doubleClick, (int X, int Y)? fixedPosition) => true;

    public (int X, int Y) GetCursorPosition() => (0, 0);

    public bool Wait(TimeSpan duration, WaitHandle cancel) =>
        duration <= TimeSpan.Zero ? !cancel.WaitOne(0) : !cancel.WaitOne(duration);

    public void Dispose() { }

    private sealed class PreviewHotkeys : IHotkeySource
    {
        public event Action? TogglePressed { add { } remove { } }
        public event Action? HoldDown { add { } remove { } }
        public event Action? HoldUp { add { } remove { } }
        public event Action? EmergencyPressed { add { } remove { } }
        public event Action? CapturePressed { add { } remove { } }

        public bool EmergencyEnabled { get; set; }
        public bool CaptureEnabled { get; set; }

        public bool Start() => true;

        public void Configure(TriggerMode mode, int keyCode, MacModifiers modifiers) { }

        public void CaptureNextKey(Action<(int KeyCode, MacModifiers Modifiers)?> onCaptured) => onCaptured(null);
    }
}
