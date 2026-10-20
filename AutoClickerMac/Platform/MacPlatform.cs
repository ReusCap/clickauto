using System.Diagnostics;
using AutoClickerMac.Core;
using static AutoClickerMac.Platform.MacNative;

namespace AutoClickerMac.Platform;

/// <summary>macOS 구현: CGEvent로 클릭을 주입하고 mach_wait_until로 정밀하게 잠든다.</summary>
internal sealed class MacPlatform : IPlatform
{
    private static readonly TimeSpan MaxWaitChunk = TimeSpan.FromMilliseconds(50);
    private readonly MachTimebaseInfo _timebase;
    private readonly MacHotkeys _hotkeys = new();

    public MacPlatform()
    {
        mach_timebase_info(out _timebase);
    }

    public bool IsPreview => false;
    public IHotkeySource Hotkeys => _hotkeys;

    public bool IsAccessibilityTrusted() => AXIsProcessTrusted() || CGPreflightPostEventAccess();

    public bool IsInputMonitoringGranted() => CGPreflightListenEventAccess();

    public void RequestPermissions()
    {
        // 처음 한 번은 시스템 안내 창이 뜨고, 시스템 설정 목록에 AutoClickerMac이 등록된다.
        if (!IsAccessibilityTrusted())
            CGRequestPostEventAccess();
        if (!IsInputMonitoringGranted())
            CGRequestListenEventAccess();
    }

    public void OpenPrivacySettings(bool inputMonitoring)
    {
        string pane = inputMonitoring ? "Privacy_ListenEvent" : "Privacy_Accessibility";
        try
        {
            Process.Start("open", $"x-apple.systempreferences:com.apple.preference.security?{pane}");
        }
        catch (Exception)
        {
            // 설정 앱을 못 열어도 안내 문구로 충분하다.
        }
    }

    public bool Click(MouseButtonKind button, bool doubleClick, (int X, int Y)? fixedPosition)
    {
        CGPoint pos;
        if (fixedPosition is { } fp)
        {
            pos = new CGPoint(fp.X, fp.Y);
            CGWarpMouseCursorPosition(pos);
        }
        else
        {
            pos = CurrentLocation();
        }

        var (downType, upType, cgButton) = button switch
        {
            MouseButtonKind.Right => (kCGEventRightMouseDown, kCGEventRightMouseUp, kCGMouseButtonRight),
            MouseButtonKind.Middle => (kCGEventOtherMouseDown, kCGEventOtherMouseUp, kCGMouseButtonCenter),
            _ => (kCGEventLeftMouseDown, kCGEventLeftMouseUp, kCGMouseButtonLeft),
        };

        int clicks = doubleClick ? 2 : 1;
        for (int state = 1; state <= clicks; state++)
        {
            // clickState를 1, 2로 올려 보내야 macOS가 더블클릭으로 인식한다.
            if (!Post(downType, pos, cgButton, state) || !Post(upType, pos, cgButton, state))
                return false;
        }
        return true;
    }

    private static bool Post(uint type, CGPoint pos, uint button, int clickState)
    {
        IntPtr evt = CGEventCreateMouseEvent(IntPtr.Zero, type, pos, button);
        if (evt == IntPtr.Zero)
            return false;
        CGEventSetIntegerValueField(evt, kCGMouseEventClickState, clickState);
        CGEventPost(kCGHIDEventTap, evt);
        CFRelease(evt);
        return true;
    }

    private static CGPoint CurrentLocation()
    {
        IntPtr evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero)
            return default;
        var p = CGEventGetLocation(evt);
        CFRelease(evt);
        return p;
    }

    public (int X, int Y) GetCursorPosition()
    {
        var p = CurrentLocation();
        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    public bool Wait(TimeSpan duration, WaitHandle cancel)
    {
        ulong deadline = mach_absolute_time() + ToMachTicks(duration);
        ulong chunk = ToMachTicks(MaxWaitChunk);
        while (true)
        {
            if (cancel.WaitOne(0))
                return false;
            ulong now = mach_absolute_time();
            if (now >= deadline)
                return true;
            // 정지 신호에 빨리 반응하도록 긴 대기는 50ms씩 나눠서 잔다.
            mach_wait_until(Math.Min(deadline, now + chunk));
        }
    }

    private ulong ToMachTicks(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
            return 0;
        double nanos = span.Ticks * 100.0;
        return (ulong)(nanos * _timebase.Denom / _timebase.Numer);
    }

    public void Dispose() => _hotkeys.Dispose();
}
