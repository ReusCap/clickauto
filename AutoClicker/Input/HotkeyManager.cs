using System.Runtime.InteropServices;
using AutoClicker.Core;
using AutoClicker.Native;

namespace AutoClicker.Input;

/// <summary>
/// 전역 단축키를 관리한다.
/// - 켜고 끄기 모드: RegisterHotKey (누를 때마다 TogglePressed)
/// - 누르고 있는 동안 모드: 저수준 키보드 훅 (HoldDown / HoldUp)
/// - 비상 정지(Esc), 좌표 지정(F8): 필요할 때만 잠깐 등록
/// </summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int ToggleId = 1;
    private const int EmergencyId = 2;
    private const int CaptureId = 3;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    private readonly NativeMethods.LowLevelKeyboardProc _hookProc; // GC가 수거하지 않도록 필드로 붙잡아 둔다
    private IntPtr _hook;
    private bool _toggleRegistered;
    private bool _emergencyRegistered;
    private bool _captureRegistered;
    private Keys _holdKey;
    private Keys _holdModifiers;
    private bool _holdDown;

    public event Action? TogglePressed;
    public event Action? EmergencyPressed;
    public event Action? CapturePressed;
    public event Action? HoldDown;
    public event Action? HoldUp;

    public HotkeyManager()
    {
        _hookProc = HookCallback;
        // 단축키 메시지만 받는 보이지 않는 창
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    /// <summary>시작/정지 단축키를 설정한다. 다른 프로그램이 이미 쓰는 조합이면 false.</summary>
    public bool SetTrigger(TriggerMode mode, Keys key, Keys modifiers)
    {
        ClearTrigger();

        if (mode == TriggerMode.Toggle)
        {
            _toggleRegistered = NativeMethods.RegisterHotKey(Handle, ToggleId, ToNativeModifiers(modifiers) | NativeMethods.MOD_NOREPEAT, (uint)key);
            return _toggleRegistered;
        }

        _holdKey = key;
        _holdModifiers = modifiers;
        _holdDown = false;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, NativeMethods.GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    /// <summary>단축키 입력을 받는 동안 기존 단축키가 가로채지 않도록 잠시 해제한다.</summary>
    public void ClearTrigger()
    {
        if (_toggleRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, ToggleId);
            _toggleRegistered = false;
        }
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _holdDown = false;
    }

    /// <summary>클릭 중에만 Esc를 가로채도록 켜고 끈다.</summary>
    public void SetEmergencyEnabled(bool enabled) =>
        SetSimpleHotkey(EmergencyId, Keys.Escape, enabled, ref _emergencyRegistered);

    public void SetCaptureEnabled(bool enabled) =>
        SetSimpleHotkey(CaptureId, Keys.F8, enabled, ref _captureRegistered);

    private void SetSimpleHotkey(int id, Keys key, bool enabled, ref bool registered)
    {
        if (enabled && !registered)
            registered = NativeMethods.RegisterHotKey(Handle, id, NativeMethods.MOD_NOREPEAT, (uint)key);
        else if (!enabled && registered)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
            registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            switch ((int)m.WParam)
            {
                case ToggleId: TogglePressed?.Invoke(); break;
                case EmergencyId: EmergencyPressed?.Invoke(); break;
                case CaptureId: CapturePressed?.Invoke(); break;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            // 프로그램이 주입한 키 입력은 무시하고, 사람이 누른 지정 키만 본다.
            if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0 && (Keys)data.vkCode == _holdKey)
            {
                int msg = (int)wParam;
                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    // 키를 누르고 있으면 반복 입력이 계속 들어오므로 처음 한 번만 처리
                    if (!_holdDown && CurrentModifiers() == _holdModifiers)
                    {
                        _holdDown = true;
                        HoldDown?.Invoke();
                    }
                }
                else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP && _holdDown)
                {
                    _holdDown = false;
                    HoldUp?.Invoke();
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static Keys CurrentModifiers()
    {
        var mods = Keys.None;
        if (NativeMethods.GetAsyncKeyState(VK_CONTROL) < 0) mods |= Keys.Control;
        if (NativeMethods.GetAsyncKeyState(VK_MENU) < 0) mods |= Keys.Alt;
        if (NativeMethods.GetAsyncKeyState(VK_SHIFT) < 0) mods |= Keys.Shift;
        return mods;
    }

    private static uint ToNativeModifiers(Keys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(Keys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(Keys.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(Keys.Shift)) result |= NativeMethods.MOD_SHIFT;
        return result;
    }

    public void Dispose()
    {
        ClearTrigger();
        SetEmergencyEnabled(false);
        SetCaptureEnabled(false);
        DestroyHandle();
    }
}
