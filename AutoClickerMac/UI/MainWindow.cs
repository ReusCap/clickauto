using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AutoClickerMac.Core;
using AutoClickerMac.Platform;

namespace AutoClickerMac.UI;

internal sealed class MainWindow : Window
{
    private const int StartDelaySec = 3;
    private const int PickDelaySec = 3;

    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x3F, 0x3F));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B));
    private static readonly IBrush PendingBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x1E));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));

    private readonly IPlatform _platform;
    private readonly ClickEngine _engine;
    private ClickerSettings _settings;

    // 권한 안내
    private readonly Border _permissionBanner = new();
    private readonly TextBlock _permissionText = Txt("");

    // 간격
    private readonly NumericUpDown _nudInterval = Nud(ClickerSettings.MinIntervalMs, ClickerSettings.MaxIntervalMs, 130);
    private readonly TextBlock _lblCps = Txt("");
    private readonly TextBlock _lblWarn = Txt("5ms 미만은 앱이 클릭을 놓치거나 느려질 수 있어요.");
    private readonly NumericUpDown _nudJitter = Nud(0, 1000, 110);

    // 클릭 옵션
    private readonly ComboBox _cmbButton = Combo("왼쪽", "오른쪽", "가운데");
    private readonly ComboBox _cmbClick = Combo("싱글 클릭", "더블 클릭");
    private readonly RadioButton _rbCursor = Radio("현재 커서 위치", "position");
    private readonly RadioButton _rbFixed = Radio("고정 좌표", "position");
    private readonly NumericUpDown _nudX = Nud(-32768, 32767, 110);
    private readonly NumericUpDown _nudY = Nud(-32768, 32767, 110);
    private readonly Button _btnPick = new();

    // 반복 / 정지
    private readonly RadioButton _rbInfinite = Radio("무한 반복", "repeat");
    private readonly RadioButton _rbCount = Radio("횟수 지정", "repeat");
    private readonly NumericUpDown _nudCount = Nud(1, 1_000_000_000, 150);
    private readonly NumericUpDown _nudMaxSec = Nud(0, 86_400, 110);

    // 단축키
    private readonly RadioButton _rbToggle = Radio("누를 때마다 켜고 끄기", "trigger");
    private readonly RadioButton _rbHold = Radio("누르고 있는 동안만", "trigger");
    private readonly Button _btnHotkey = new() { MinWidth = 120 };
    private readonly CheckBox _chkTopMost = new() { Content = "항상 위에 표시" };

    // 상태
    private readonly TextBlock _lblStopHelp = Txt("");
    private readonly TextBlock _lblStatus = Txt("대기 중");
    private readonly TextBlock _lblStats = Txt("");
    private readonly Button _btnStartStop = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 44 };
    private readonly StackPanel _options = new() { Spacing = 10 };

    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _pickTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<(long Ms, long Clicks)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private int _hotkeyKeyCode;
    private MacModifiers _hotkeyMods;
    private bool _capturingHotkey;
    private bool _hotkeysOk;
    private int _startCountdown;
    private int _pickCountdown;

    public MainWindow()
    {
        _platform = OperatingSystem.IsMacOS() ? new MacPlatform() : new PreviewPlatform();
        _engine = new ClickEngine(_platform);
        _settings = SettingsStore.Load();

        Title = "AutoClicker";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildLayout();
        ApplySettingsToUi(_settings);
        WireEvents();

        ApplyTrigger();
        _platform.RequestPermissions();
        RefreshPermission();
        UpdateIntervalInfo();
        UpdateIdleUi(null);
    }

    // ---------------- 화면 배치 ----------------

    private Control BuildLayout()
    {
        _lblWarn.Foreground = WarnBrush;
        _lblStatus.FontSize = 16;
        _lblStatus.FontWeight = FontWeight.Bold;
        _btnStartStop.FontWeight = FontWeight.Bold;

        // 권한 안내 배너
        var openAccessibility = new Button { Content = "손쉬운 사용 열기" };
        openAccessibility.Click += (_, _) => _platform.OpenPrivacySettings(inputMonitoring: false);
        var openInputMonitoring = new Button { Content = "입력 모니터링 열기" };
        openInputMonitoring.Click += (_, _) => _platform.OpenPrivacySettings(inputMonitoring: true);
        var recheck = new Button { Content = "다시 확인" };
        recheck.Click += (_, _) => RefreshPermission();
        _permissionText.TextWrapping = TextWrapping.Wrap;
        _permissionText.MaxWidth = 460;
        _permissionBanner.Child = new StackPanel { Spacing = 8, Children = { _permissionText, Row(openAccessibility, openInputMonitoring, recheck) } };
        _lblStopHelp.TextWrapping = TextWrapping.Wrap;
        _lblStopHelp.MaxWidth = 480;
        _permissionBanner.Padding = new Thickness(12, 10);
        _permissionBanner.CornerRadius = new CornerRadius(8);
        _permissionBanner.BorderThickness = new Thickness(1);
        _permissionBanner.BorderBrush = WarnBrush;

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (int ms in new[] { 100, 50, 10, 5, 1 })
        {
            var b = new Button { Content = $"{ms}ms" };
            b.Click += (_, _) => _nudInterval.Value = ms;
            presets.Children.Add(b);
        }

        _options.Children.Add(Group("클릭 간격",
            Row(Txt("간격"), _nudInterval, Txt("ms"), _lblCps),
            presets,
            Row(Txt("랜덤 흔들림 ±"), _nudJitter, Txt("ms")),
            _lblWarn));

        _options.Children.Add(Group("클릭 옵션",
            Row(Txt("버튼"), _cmbButton, Txt("종류"), _cmbClick),
            _rbCursor,
            Row(_rbFixed, Txt("X"), _nudX, Txt("Y"), _nudY, _btnPick)));

        _options.Children.Add(Group("반복 / 자동 정지",
            Row(_rbInfinite, _rbCount, _nudCount, Txt("회")),
            Row(_nudMaxSec, Txt("초 후 자동 정지 (0 = 제한 없음)"))));

        _options.Children.Add(Group("단축키",
            Row(_rbToggle, _rbHold),
            Row(Txt("시작/정지 키"), _btnHotkey, Txt("← 눌러서 변경")),
            Txt("맥북은 F키를 누를 때 fn을 같이 눌러야 해요 (예: fn + F6)"),
            Txt("비상 정지: Esc (클릭 중에만 동작)"),
            _chkTopMost));

        var root = new StackPanel { Spacing = 10, Margin = new Thickness(16), MinWidth = 500 };
        if (_platform.IsPreview)
        {
            root.Children.Add(new TextBlock
            {
                Text = "미리보기 모드: 맥이 아니라서 실제로 클릭하지 않고, 단축키도 동작하지 않습니다.",
                Foreground = PendingBrush,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480,
            });
        }
        root.Children.Add(_permissionBanner);
        root.Children.Add(_options);
        root.Children.Add(_lblStopHelp);
        root.Children.Add(_lblStatus);
        root.Children.Add(_lblStats);
        root.Children.Add(_btnStartStop);
        return root;
    }

    private void WireEvents()
    {
        _nudInterval.ValueChanged += (_, _) => UpdateIntervalInfo();
        _rbFixed.IsCheckedChanged += (_, _) => UpdateEnabledStates();
        _rbCount.IsCheckedChanged += (_, _) => UpdateEnabledStates();
        _rbToggle.IsCheckedChanged += (_, _) => { if (_rbToggle.IsChecked == true) ApplyTrigger(); };
        _rbHold.IsCheckedChanged += (_, _) => { if (_rbHold.IsChecked == true) ApplyTrigger(); };
        _chkTopMost.IsCheckedChanged += (_, _) => Topmost = _chkTopMost.IsChecked == true;

        _btnHotkey.Click += (_, _) => BeginHotkeyCapture();
        _btnPick.Click += (_, _) => BeginPick();
        _pickTimer.Tick += (_, _) => { if (--_pickCountdown <= 0) CapturePosition(); else UpdatePickButton(); };

        _btnStartStop.Click += (_, _) => OnStartStopButton();
        _countdownTimer.Tick += (_, _) => { if (--_startCountdown <= 0) StartClicking(); else UpdateIdleUi(null); };
        _uiTimer.Tick += (_, _) => UpdateStats();

        _engine.StateChanged += OnEngineStateChanged;

        // 단축키 이벤트는 키 감시 스레드에서 오므로 UI 스레드로 넘긴다.
        var hk = _platform.Hotkeys;
        hk.TogglePressed += () => Ui(() => { if (_engine.IsRunning) _engine.Stop(); else StartClicking(); });
        hk.HoldDown += () => Ui(StartClicking);
        hk.HoldUp += () => _engine.Stop();
        hk.EmergencyPressed += () => { _engine.Stop(StopReason.Emergency); Ui(CancelCountdown); };
        hk.CapturePressed += () => Ui(CapturePosition);
    }

    private static void Ui(Action action) => Dispatcher.UIThread.Post(action);

    // ---------------- 권한 ----------------

    private bool RefreshPermission()
    {
        bool trusted = _platform.IsAccessibilityTrusted();
        // 이벤트 탭이 만들어져도 입력 모니터링 권한이 없으면 키 입력이 오지 않으므로 둘 다 확인한다.
        _hotkeysOk = _platform.Hotkeys.Start() && _platform.IsInputMonitoringGranted();
        _permissionBanner.IsVisible = !(trusted && _hotkeysOk);

        var missing = new List<string>();
        if (!trusted)
            missing.Add("· 손쉬운 사용: 클릭에 필요");
        if (!_hotkeysOk)
            missing.Add("· 입력 모니터링: 단축키(F6, Esc)에 필요");
        _permissionText.Text =
            "시스템 설정 → 개인정보 보호 및 보안에서 AutoClickerMac을 켜주세요.\n"
            + string.Join("\n", missing)
            + "\n목록에 없으면 아래 + 버튼으로 응용 프로그램 폴더의 AutoClickerMac을 추가하세요."
            + "\n켠 뒤에는 앱을 완전히 종료(⌘Q)하고 다시 실행해야 적용될 수 있어요.";

        UpdateStopHelp();
        return trusted;
    }

    private void UpdateStopHelp()
    {
        if (_hotkeysOk)
        {
            _lblStopHelp.Text = $"정지: {HotkeyText()} · Esc · 마우스를 화면 왼쪽 위 모서리로 (고정 좌표면 마우스를 움직이기)";
            _lblStopHelp.Foreground = Foreground;
        }
        else
        {
            _lblStopHelp.Text = "⚠ 지금은 단축키가 동작하지 않아요. 클릭을 멈추려면 마우스를 화면 왼쪽 위 모서리로 옮기세요. (고정 좌표면 마우스를 움직이기)";
            _lblStopHelp.Foreground = WarnBrush;
        }
    }

    // ---------------- 시작 / 정지 ----------------

    private void OnStartStopButton()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
        }
        else if (_countdownTimer.IsEnabled)
        {
            CancelCountdown();
        }
        else
        {
            // 버튼을 마우스로 눌렀으면 커서가 버튼 위에 있으므로, 원하는 곳으로 옮길 시간을 준다.
            _startCountdown = StartDelaySec;
            _countdownTimer.Start();
            UpdateIdleUi(null);
        }
    }

    private void StartClicking()
    {
        CancelCountdown();
        if (_engine.IsRunning || _capturingHotkey)
            return;
        if (!RefreshPermission())
        {
            _lblStatus.Text = "권한이 없어서 시작할 수 없어요";
            _lblStatus.Foreground = WarnBrush;
            return;
        }
        _settings = ReadSettingsFromUi();
        if (_settings.Position == PositionMode.Fixed
            && _settings.FixedX <= ClickEngine.FailsafeCornerPx && _settings.FixedY <= ClickEngine.FailsafeCornerPx)
        {
            _lblStatus.Text = "고정 좌표가 비상 정지 모서리(왼쪽 위)에 있어요. 다른 좌표를 지정하세요.";
            _lblStatus.Foreground = WarnBrush;
            return;
        }
        _engine.Start(_settings);
    }

    private void CancelCountdown()
    {
        if (!_countdownTimer.IsEnabled)
            return;
        _countdownTimer.Stop();
        if (!_engine.IsRunning)
            UpdateIdleUi(null);
    }

    private void OnEngineStateChanged(bool running, StopReason? reason)
    {
        Ui(() =>
        {
            if (running)
            {
                _platform.Hotkeys.EmergencyEnabled = true;
                _options.IsEnabled = false;
                _samples.Clear();
                _lblStatus.Text = "클릭 중...";
                _lblStatus.Foreground = OkBrush;
                _btnStartStop.Content = $"정지  ({HotkeyText()} / Esc)";
                _uiTimer.Start();
            }
            else
            {
                _uiTimer.Stop();
                UpdateStats();
                _platform.Hotkeys.EmergencyEnabled = false;
                _options.IsEnabled = true;
                UpdateIdleUi(reason);
            }
        });
    }

    private void UpdateIdleUi(StopReason? reason)
    {
        if (_countdownTimer.IsEnabled)
        {
            _lblStatus.Text = $"{_startCountdown}초 후 시작 - 클릭할 곳에 커서를 두세요";
            _lblStatus.Foreground = PendingBrush;
            _btnStartStop.Content = "취소";
            return;
        }

        _lblStatus.Text = reason switch
        {
            StopReason.Emergency => "비상 정지됨 (Esc)",
            StopReason.CountReached => "설정한 횟수를 모두 클릭함",
            StopReason.DurationReached => "최대 시간이 지나 정지함",
            StopReason.CornerFailsafe => "마우스를 모서리로 옮겨 정지함",
            StopReason.MouseMovedFailsafe => "마우스를 움직여 정지함",
            StopReason.Error => "오류로 정지함",
            StopReason.User => "정지됨",
            _ => "대기 중",
        };
        _lblStatus.Foreground = reason is StopReason.Emergency or StopReason.Error ? WarnBrush : Foreground;
        _btnStartStop.Content = $"시작  ({HotkeyText()} 또는 {StartDelaySec}초 후)";
    }

    private void UpdateStats()
    {
        long now = _clock.ElapsedMilliseconds;
        long total = _engine.TotalClicks;
        _samples.Enqueue((now, total));
        while (_samples.Count > 2 && now - _samples.Peek().Ms > 1000)
            _samples.Dequeue();

        var first = _samples.Peek();
        double seconds = (now - first.Ms) / 1000.0;
        double cps = seconds > 0 ? (total - first.Clicks) / seconds : 0;

        string text = $"실측 {cps:0.0} CPS · 총 {total:N0}회";
        long failed = _engine.FailedClicks;
        if (failed > 0)
            text += $" · 전달 안 됨 {failed:N0}회";
        _lblStats.Text = text;
    }

    // ---------------- 단축키 ----------------

    private void ApplyTrigger()
    {
        var mode = _rbHold.IsChecked == true ? TriggerMode.Hold : TriggerMode.Toggle;
        _platform.Hotkeys.Configure(mode, _hotkeyKeyCode, _hotkeyMods);
        _btnHotkey.Content = HotkeyText();
        UpdateStopHelp();
        if (!_engine.IsRunning)
            UpdateIdleUi(null);
    }

    private void BeginHotkeyCapture()
    {
        if (_engine.IsRunning || _capturingHotkey)
            return;
        if (_platform.IsPreview)
        {
            _btnHotkey.Content = $"{HotkeyText()} (미리보기에선 변경 불가)";
            return;
        }
        _capturingHotkey = true;
        _btnHotkey.Content = "키 조합을 누르세요 (Esc 취소)";
        _platform.Hotkeys.CaptureNextKey(result => Ui(() =>
        {
            _capturingHotkey = false;
            if (result is { } r)
            {
                _hotkeyKeyCode = r.KeyCode;
                _hotkeyMods = r.Modifiers;
            }
            ApplyTrigger();
        }));
    }

    private string HotkeyText() => MacKeys.Format(_hotkeyKeyCode, _hotkeyMods);

    // ---------------- 좌표 지정 ----------------

    private void BeginPick()
    {
        _pickCountdown = PickDelaySec;
        _platform.Hotkeys.CaptureEnabled = true;
        _pickTimer.Start();
        UpdatePickButton();
    }

    private void CapturePosition()
    {
        if (!_pickTimer.IsEnabled)
            return;
        _pickTimer.Stop();
        _platform.Hotkeys.CaptureEnabled = false;
        var (x, y) = _platform.GetCursorPosition();
        _nudX.Value = Math.Clamp(x, (int)_nudX.Minimum, (int)_nudX.Maximum);
        _nudY.Value = Math.Clamp(y, (int)_nudY.Minimum, (int)_nudY.Maximum);
        _rbFixed.IsChecked = true;
        UpdatePickButton();
    }

    private void UpdatePickButton() =>
        _btnPick.Content = _pickTimer.IsEnabled ? $"{_pickCountdown}초 뒤 지정 (F8 = 지금)" : "좌표 지정";

    // ---------------- 설정 <-> 화면 ----------------

    private void ApplySettingsToUi(ClickerSettings s)
    {
        _nudInterval.Value = s.IntervalMs;
        _nudJitter.Value = s.JitterMs;
        _cmbButton.SelectedIndex = (int)s.Button;
        _cmbClick.SelectedIndex = (int)s.Click;
        (s.Position == PositionMode.Fixed ? _rbFixed : _rbCursor).IsChecked = true;
        _nudX.Value = Math.Clamp(s.FixedX, (int)_nudX.Minimum, (int)_nudX.Maximum);
        _nudY.Value = Math.Clamp(s.FixedY, (int)_nudY.Minimum, (int)_nudY.Maximum);
        (s.RepeatCount > 0 ? _rbCount : _rbInfinite).IsChecked = true;
        _nudCount.Value = Math.Clamp(s.RepeatCount > 0 ? s.RepeatCount : 100, (long)_nudCount.Minimum, (long)_nudCount.Maximum);
        _nudMaxSec.Value = s.MaxDurationSec;
        (s.Trigger == TriggerMode.Hold ? _rbHold : _rbToggle).IsChecked = true;
        _hotkeyKeyCode = s.HotkeyKeyCode;
        _hotkeyMods = s.HotkeyModifiers;
        _chkTopMost.IsChecked = s.AlwaysOnTop;
        Topmost = s.AlwaysOnTop;
        UpdatePickButton();
        UpdateEnabledStates();
    }

    private ClickerSettings ReadSettingsFromUi() => new()
    {
        IntervalMs = IntValue(_nudInterval),
        JitterMs = IntValue(_nudJitter),
        Button = (MouseButtonKind)Math.Max(0, _cmbButton.SelectedIndex),
        Click = (ClickKind)Math.Max(0, _cmbClick.SelectedIndex),
        Position = _rbFixed.IsChecked == true ? PositionMode.Fixed : PositionMode.Cursor,
        FixedX = IntValue(_nudX),
        FixedY = IntValue(_nudY),
        RepeatCount = _rbCount.IsChecked == true ? (long)(_nudCount.Value ?? 1) : 0,
        MaxDurationSec = IntValue(_nudMaxSec),
        Trigger = _rbHold.IsChecked == true ? TriggerMode.Hold : TriggerMode.Toggle,
        HotkeyKeyCode = _hotkeyKeyCode,
        HotkeyModifiers = _hotkeyMods,
        AlwaysOnTop = _chkTopMost.IsChecked == true,
    };

    private static int IntValue(NumericUpDown nud) => (int)(nud.Value ?? nud.Minimum);

    private void UpdateIntervalInfo()
    {
        int ms = Math.Max(1, IntValue(_nudInterval));
        _lblCps.Text = $"≈ 초당 {1000.0 / ms:0.#}회";
        _lblWarn.IsVisible = ms < 5;
    }

    private void UpdateEnabledStates()
    {
        _nudX.IsEnabled = _nudY.IsEnabled = _rbFixed.IsChecked == true;
        _nudCount.IsEnabled = _rbCount.IsChecked == true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.Dispose();
        _platform.Dispose();
        _uiTimer.Stop();
        _countdownTimer.Stop();
        _pickTimer.Stop();
        SettingsStore.Save(ReadSettingsFromUi());
        base.OnClosing(e);
    }

    // ---------------- 컨트롤 생성 도우미 ----------------

    private static TextBlock Txt(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static NumericUpDown Nud(decimal min, decimal max, double width) =>
        new() { Minimum = min, Maximum = max, Value = min, Increment = 1, FormatString = "0", Width = width };

    private static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { MinWidth = 110 };
        foreach (var item in items)
            c.Items.Add(item);
        return c;
    }

    // GroupName으로 묶으면 서로 다른 줄에 있어도 하나만 선택된다.
    private static RadioButton Radio(string text, string group) =>
        new() { Content = text, GroupName = group, VerticalAlignment = VerticalAlignment.Center };

    private static StackPanel Row(params Control[] controls)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in controls)
            p.Children.Add(c);
        return p;
    }

    private static Border Group(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        foreach (var r in rows)
            stack.Children.Add(r);
        return new Border
        {
            Child = stack,
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = LineBrush,
        };
    }
}
