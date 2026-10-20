using System.Diagnostics;
using AutoClickerMac.Platform;

namespace AutoClickerMac.Core;

internal enum StopReason { User, Emergency, CountReached, DurationReached, CornerFailsafe, MouseMovedFailsafe, Error }

/// <summary>
/// 전용 스레드에서 정해진 간격으로 클릭을 주입한다. (Windows판 ClickEngine과 같은 스케줄링)
/// 절대 시각 기준으로 다음 클릭을 예약해 오차가 쌓이지 않고, 한참 밀리면 몰아서 쏘지 않는다.
/// </summary>
internal sealed class ClickEngine : IDisposable
{
    /// <summary>커서가 화면 왼쪽 위 이 픽셀 안에 들어오면 정지한다.</summary>
    public const int FailsafeCornerPx = 5;

    /// <summary>고정 좌표 모드에서 커서가 이만큼 벗어나면 사람이 움직인 것으로 보고 정지한다.</summary>
    public const int FailsafeMovePx = 30;

    private readonly IPlatform _platform;
    private readonly object _gate = new();
    private readonly ManualResetEvent _stopSignal = new(false);
    private Thread? _thread;
    private volatile bool _running;
    private volatile StopReason _pendingReason;
    private long _totalClicks;
    private long _failedClicks;

    /// <summary>시작/정지 시 클릭 스레드에서 발생한다. 정지일 때만 이유가 들어간다.</summary>
    public event Action<bool, StopReason?>? StateChanged;

    public ClickEngine(IPlatform platform)
    {
        _platform = platform;
    }

    public bool IsRunning => _running;
    public long TotalClicks => Interlocked.Read(ref _totalClicks);
    public long FailedClicks => Interlocked.Read(ref _failedClicks);

    public bool Start(ClickerSettings settings)
    {
        lock (_gate)
        {
            if (_running)
                return false;

            _thread?.Join();

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
        try
        {
            bool doubleClick = s.Click == ClickKind.Double;
            (int, int)? fixedPos = s.Position == PositionMode.Fixed ? (s.FixedX, s.FixedY) : null;
            double freq = Stopwatch.Frequency;
            double intervalTicks = s.IntervalMs * freq / 1000.0;
            double jitterTicks = s.JitterMs * freq / 1000.0;
            double maxTicks = s.MaxDurationSec * freq;
            var rng = new Random();

            var clock = Stopwatch.StartNew();
            double next = 0;
            bool clickedOnce = false;

            while (true)
            {
                if (_stopSignal.WaitOne(0))
                {
                    reason = _pendingReason;
                    break;
                }

                // 단축키가 안 먹혀도 멈출 수 있도록, 권한이 필요 없는 마우스 위치로 비상 정지를 판단한다.
                var (cx, cy) = _platform.GetCursorPosition();
                if (cx <= FailsafeCornerPx && cy <= FailsafeCornerPx)
                {
                    reason = StopReason.CornerFailsafe;
                    break;
                }
                if (fixedPos is { } fp && clickedOnce
                    && (Math.Abs(cx - fp.Item1) > FailsafeMovePx || Math.Abs(cy - fp.Item2) > FailsafeMovePx))
                {
                    reason = StopReason.MouseMovedFailsafe; // 고정 좌표로 옮겨 둔 커서를 사람이 움직였다
                    break;
                }

                clickedOnce = true;
                if (_platform.Click(s.Button, doubleClick, fixedPos))
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
                if (next < now - intervalTicks)
                    next = now;

                double target = next;
                if (jitterTicks > 0)
                    target += (rng.NextDouble() * 2 - 1) * jitterTicks;
                if (maxTicks > 0)
                    target = Math.Min(target, maxTicks);

                double waitTicks = target - now;
                if (waitTicks > 0 && !_platform.Wait(TimeSpan.FromSeconds(waitTicks / freq), _stopSignal))
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
            _running = false;
        }

        StateChanged?.Invoke(false, reason);
    }

    public void Dispose()
    {
        Stop();
        _thread?.Join(1000);
        _stopSignal.Dispose();
    }
}
