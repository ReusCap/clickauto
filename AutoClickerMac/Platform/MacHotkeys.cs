using AutoClickerMac.Core;
using static AutoClickerMac.Platform.MacNative;

namespace AutoClickerMac.Platform;

/// <summary>
/// CGEventTap(듣기 전용)으로 전역 키 입력을 감시한다.
/// 탭은 별도 스레드의 CFRunLoop에서 돌고, 이벤트도 그 스레드에서 발생한다.
/// </summary>
internal sealed class MacHotkeys : IHotkeySource, IDisposable
{
    private const ulong ModifierMask = kCGEventFlagMaskShift | kCGEventFlagMaskControl | kCGEventFlagMaskAlternate | kCGEventFlagMaskCommand;

    private readonly CGEventTapCallBack _callback; // 네이티브 쪽이 붙잡고 있으므로 GC되지 않게 필드로 보관
    private readonly object _gate = new();
    private Thread? _thread;
    private IntPtr _tap;
    private IntPtr _runLoop;

    private volatile TriggerMode _mode = TriggerMode.Toggle;
    private volatile int _keyCode = MacKeys.F6;
    private volatile MacModifiers _modifiers;
    private volatile bool _holdDown;
    private Action<(int KeyCode, MacModifiers Modifiers)?>? _captureCallback;

    public event Action? TogglePressed;
    public event Action? HoldDown;
    public event Action? HoldUp;
    public event Action? EmergencyPressed;
    public event Action? CapturePressed;

    public bool EmergencyEnabled { get; set; }
    public bool CaptureEnabled { get; set; }

    public MacHotkeys()
    {
        _callback = OnEvent;
    }

    public bool Start()
    {
        lock (_gate)
        {
            if (_tap != IntPtr.Zero)
                return true;

            bool ok = false;
            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                ulong mask = (1UL << (int)kCGEventKeyDown) | (1UL << (int)kCGEventKeyUp) | (1UL << (int)kCGEventFlagsChanged);
                IntPtr tap = CGEventTapCreate(kCGSessionEventTap, kCGHeadInsertEventTap, kCGEventTapOptionListenOnly, mask, _callback, IntPtr.Zero);
                if (tap == IntPtr.Zero)
                {
                    // 손쉬운 사용 / 입력 모니터링 권한이 없으면 실패한다.
                    ready.Set();
                    return;
                }

                IntPtr source = CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
                IntPtr mode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", kCFStringEncodingUTF8);
                _runLoop = CFRunLoopGetCurrent();
                CFRunLoopAddSource(_runLoop, source, mode);
                CGEventTapEnable(tap, true);
                _tap = tap;
                ok = true;
                ready.Set();

                CFRunLoopRun(); // Dispose에서 CFRunLoopStop을 부를 때까지 여기서 이벤트를 처리

                CGEventTapEnable(tap, false);
                CFRelease(source);
                CFRelease(mode);
                CFRelease(tap);
            })
            {
                IsBackground = true,
                Name = "HotkeyTap",
            };
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(3));
            return ok;
        }
    }

    public void Configure(TriggerMode mode, int keyCode, MacModifiers modifiers)
    {
        _mode = mode;
        _keyCode = keyCode;
        _modifiers = modifiers;
        _holdDown = false;
    }

    public void CaptureNextKey(Action<(int KeyCode, MacModifiers Modifiers)?> onCaptured) =>
        Volatile.Write(ref _captureCallback, onCaptured);

    private IntPtr OnEvent(IntPtr proxy, uint type, IntPtr cgEvent, IntPtr userInfo)
    {
        // 네이티브 콜백 밖으로 예외가 나가면 앱이 죽으므로 전부 잡는다.
        try
        {
            if (type is kCGEventTapDisabledByTimeout or kCGEventTapDisabledByUserInput)
            {
                if (_tap != IntPtr.Zero)
                    CGEventTapEnable(_tap, true); // 시스템이 탭을 껐으면 다시 켠다
            }
            else if (type == kCGEventKeyDown)
            {
                if (CGEventGetIntegerValueField(cgEvent, kCGKeyboardEventAutorepeat) == 0)
                    OnKeyDown((int)CGEventGetIntegerValueField(cgEvent, kCGKeyboardEventKeycode), ToModifiers(CGEventGetFlags(cgEvent)));
            }
            else if (type == kCGEventKeyUp)
            {
                OnKeyUp((int)CGEventGetIntegerValueField(cgEvent, kCGKeyboardEventKeycode));
            }
        }
        catch (Exception)
        {
            // 무시
        }
        return cgEvent;
    }

    private void OnKeyDown(int keyCode, MacModifiers modifiers)
    {
        var capture = Interlocked.Exchange(ref _captureCallback, null);
        if (capture is not null)
        {
            capture(keyCode == MacKeys.Escape ? null : (keyCode, modifiers));
            return;
        }

        if (keyCode == MacKeys.Escape && EmergencyEnabled)
        {
            EmergencyPressed?.Invoke();
            return;
        }
        if (keyCode == MacKeys.F8 && CaptureEnabled)
        {
            CapturePressed?.Invoke();
            return;
        }

        if (keyCode != _keyCode || modifiers != _modifiers)
            return;

        if (_mode == TriggerMode.Toggle)
        {
            TogglePressed?.Invoke();
        }
        else if (!_holdDown)
        {
            _holdDown = true;
            HoldDown?.Invoke();
        }
    }

    private void OnKeyUp(int keyCode)
    {
        if (_mode == TriggerMode.Hold && _holdDown && keyCode == _keyCode)
        {
            _holdDown = false;
            HoldUp?.Invoke();
        }
    }

    private static MacModifiers ToModifiers(ulong flags)
    {
        flags &= ModifierMask; // fn, 숫자패드 같은 다른 플래그는 무시
        var mods = MacModifiers.None;
        if ((flags & kCGEventFlagMaskShift) != 0) mods |= MacModifiers.Shift;
        if ((flags & kCGEventFlagMaskControl) != 0) mods |= MacModifiers.Control;
        if ((flags & kCGEventFlagMaskAlternate) != 0) mods |= MacModifiers.Option;
        if ((flags & kCGEventFlagMaskCommand) != 0) mods |= MacModifiers.Command;
        return mods;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_runLoop != IntPtr.Zero)
            {
                CFRunLoopStop(_runLoop);
                _runLoop = IntPtr.Zero;
            }
            _thread?.Join(1000);
            _tap = IntPtr.Zero;
        }
    }
}
