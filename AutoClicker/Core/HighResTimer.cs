using AutoClicker.Native;

namespace AutoClicker.Core;

/// <summary>
/// 바쁜 대기 없이 1ms 미만 정밀도로 잠드는 타이머.
/// 고해상도 대기 타이머(Windows 10 1803+)를 쓰고, 지원하지 않으면 일반 대기 타이머로 대체한다.
/// </summary>
internal sealed class HighResTimer : IDisposable
{
    private IntPtr _handle;

    public bool IsHighResolution { get; }

    public HighResTimer()
    {
        _handle = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
        IsHighResolution = _handle != IntPtr.Zero;

        if (!IsHighResolution)
        {
            _handle = NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null, 0, NativeMethods.TIMER_ALL_ACCESS);
            if (_handle == IntPtr.Zero)
                throw NativeMethods.LastError("CreateWaitableTimerExW");
        }
    }

    /// <summary>
    /// <paramref name="duration"/> 동안 잠든다. 그 사이 <paramref name="cancel"/>이 신호되면 즉시 깨어나 false를 반환한다.
    /// </summary>
    public bool Wait(TimeSpan duration, WaitHandle cancel)
    {
        if (duration <= TimeSpan.Zero)
            return !cancel.WaitOne(0);

        long dueTime = -duration.Ticks; // 음수 = 지금부터의 상대 시간 (100ns 단위)
        if (!NativeMethods.SetWaitableTimer(_handle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw NativeMethods.LastError("SetWaitableTimer");

        var handles = new[] { _handle, cancel.SafeWaitHandle.DangerousGetHandle() };
        uint result = NativeMethods.WaitForMultipleObjects((uint)handles.Length, handles, false, NativeMethods.INFINITE);
        GC.KeepAlive(cancel);
        return result == NativeMethods.WAIT_OBJECT_0;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
