using System.Diagnostics;
using AutoClicker.Native;

namespace AutoClicker.Core;

internal enum StopReason { User, Emergency, CountReached, DurationReached, Error }

/// <summary>
/// 전용 스레드에서 정해진 간격으로 클릭을 주입한다.
/// 절대 시각 기준으로 다음 클릭을 예약해 오차가 쌓이지 않고, 고해상도 타이머로 잠들어 CPU를 거의 쓰지 않는다.
/// </summary>
internal sealed class ClickEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly ManualResetEvent _stopSignal = new(false);
    private Thread? _thread;
    private volatile bool _running;
    private volatile StopReason _pendingReason;
    private long _totalClicks;
    private long _failedClicks;

    /// <summary>시작/정지 시 클릭 스레드에서 발생한다. 정지일 때만 이유가 들어간다.</summary>
    public event Action<bool, StopReason?>? StateChanged;

    public bool IsRunning => _running;
    public long TotalClicks => Interlocked.Read(ref _totalClicks);

    /// <summary>다른 프로그램(관리자 권한 창 등)이 입력을 막아 전달되지 않은 클릭 수.</summary>
    public long FailedClicks => Interlocked.Read(ref _failedClicks);

    public bool Start(ClickerSettings settings)
    {
        lock (_gate)
        {
            if (_running)
                return false;

            _thread?.Join(); // 직전 실행의 정리가 끝나길 기다린다 (아주 짧음)

            var snapshot = settings.Clone();
            snapshot.Normalize();

            _stopSignal.Reset();
            _pendingReason = StopReason.User;
            Interlocked.Exchange(ref _totalClicks, 0);
            Interlocked.Exchange(ref _failedClicks, 0);
            _running = true;

            _thread = new Thread(() => Run(snapshot))
            {
                IsBackground = true,
                Name = "ClickEngine",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
        }
        StateChanged?.Invoke(true, null);
        return true;
    }

    public void Stop(StopReason reason = StopReason.User)
    {
        if (!_running)
            return;
        _pendingReason = reason;
        _stopSignal.Set();
    }

    private void Run(ClickerSettings s)
    {
        var reason = StopReason.User;
        HighResTimer? timer = null;
        bool raisedPeriod = false;

        try
        {
            timer = new HighResTimer();
            // 고해상도 타이머를 못 쓰는 환경에서만 시스템 타이머 해상도를 1ms로 올린다 (실행 중에만).
            if (!timer.IsHighResolution)
                raisedPeriod = NativeMethods.timeBeginPeriod(1) == 0;

            var inputs = BuildInputs(s);
            double freq = Stopwatch.Frequency;
            double intervalTicks = s.IntervalMs * freq / 1000.0;
            double jitterTicks = s.JitterMs * freq / 1000.0;
            double maxTicks = s.MaxDurationSec * freq;
            var rng = new Random();

            var clock = Stopwatch.StartNew();
            double next = 0;

            while (true)
            {
                if (_stopSignal.WaitOne(0))
                {
                    reason = _pendingReason;
                    break;
                }

                if (s.Position == PositionMode.Fixed)
                    NativeMethods.SetCursorPos(s.FixedX, s.FixedY);

                // 누름+뗌(더블이면 4개)을 한 번의 호출로 보낸다.
                if (NativeMethods.SendInput((uint)inputs.Length, inputs, NativeMethods.InputSize) == inputs.Length)
                    Interlocked.Increment(ref _totalClicks);
                else
                    Interlocked.Increment(ref _failedClicks);

                if (s.RepeatCount > 0 && TotalClicks >= s.RepeatCount)
                {
                    reason = StopReason.CountReached;
                    break;
                }

                double now = clock.ElapsedTicks;
                if (maxTicks > 0 && now >= maxTicks)
                {
                    reason = StopReason.DurationReached;
                    break;
                }

                next += intervalTicks;
                // 한 간격 이상 밀렸으면 몰아서 쏘지 않고 기준을 지금으로 다시 잡는다.
                if (next < now - intervalTicks)
                    next = now;

                double target = next;
                if (jitterTicks > 0)
                    target += (rng.NextDouble() * 2 - 1) * jitterTicks;
                if (maxTicks > 0)
                    target = Math.Min(target, maxTicks);

                double waitTicks = target - now;
                if (waitTicks > 0 && !timer.Wait(TimeSpan.FromSeconds(waitTicks / freq), _stopSignal))
                {
                    reason = _pendingReason;
                    break;
                }

                if (maxTicks > 0 && clock.ElapsedTicks >= maxTicks)
                {
                    reason = StopReason.DurationReached;
                    break;
                }
            }
        }
        catch (Exception)
        {
            reason = StopReason.Error;
        }
        finally
        {
            if (raisedPeriod)
                NativeMethods.timeEndPeriod(1);
            timer?.Dispose();
            _running = false;
        }

        StateChanged?.Invoke(false, reason);
    }

    private static NativeMethods.INPUT[] BuildInputs(ClickerSettings s)
    {
        var (down, up) = s.Button switch
        {
            MouseButtonKind.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
            MouseButtonKind.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
            _ => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP),
        };

        int clicks = s.Click == ClickKind.Double ? 2 : 1;
        var inputs = new NativeMethods.INPUT[clicks * 2];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].type = NativeMethods.INPUT_MOUSE;
            inputs[i].mi.dwFlags = i % 2 == 0 ? down : up;
        }
        return inputs;
    }

    public void Dispose()
    {
        Stop();
        _thread?.Join(1000);
        _stopSignal.Dispose();
    }
}
