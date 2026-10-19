using System.Diagnostics;
using AutoClicker.Core;
using AutoClicker.Input;
using AutoClicker.Native;

namespace AutoClicker.UI;

internal sealed class MainForm : Form
{
    private const int StartDelaySec = 3;
    private const int PickDelaySec = 3;

    private readonly ClickEngine _engine = new();
    private readonly HotkeyManager _hotkeys = new();
    private ClickerSettings _settings;

    // 간격
    private readonly NumericUpDown _nudInterval = Nud(ClickerSettings.MinIntervalMs, ClickerSettings.MaxIntervalMs, 90);
    private readonly Label _lblCps = Lbl("");
    private readonly Label _lblWarn = Lbl("5ms 미만은 앱이 클릭을 놓치거나 느려질 수 있어요.");
    private readonly NumericUpDown _nudJitter = Nud(0, 1000, 70);

    // 클릭 옵션
    private readonly ComboBox _cmbButton = Combo("왼쪽", "오른쪽", "가운데");
    private readonly ComboBox _cmbClick = Combo("싱글 클릭", "더블 클릭");
    private readonly RadioButton _rbCursor = Radio("현재 커서 위치");
    private readonly RadioButton _rbFixed = Radio("고정 좌표");
    private readonly NumericUpDown _nudX = Nud(-32768, 32767, 70);
    private readonly NumericUpDown _nudY = Nud(-32768, 32767, 70);
    private readonly Button _btnPick = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };

    // 반복 / 정지
    private readonly RadioButton _rbInfinite = Radio("무한 반복");
    private readonly RadioButton _rbCount = Radio("횟수 지정");
    private readonly NumericUpDown _nudCount = Nud(1, 1_000_000_000, 110);
    private readonly NumericUpDown _nudMaxSec = Nud(0, 86_400, 80);

    // 단축키
    private readonly RadioButton _rbToggle = Radio("누를 때마다 켜고 끄기");
    private readonly RadioButton _rbHold = Radio("누르고 있는 동안만");
    private readonly TextBox _txtHotkey = new() { ReadOnly = true, Width = 160, Cursor = Cursors.Hand, TabStop = false };
    private readonly Label _lblHotkeyHint = Lbl("");
    private readonly CheckBox _chkTopMost = new() { Text = "항상 위에 표시", AutoSize = true };

    // 상태
    private readonly Label _lblStatus = Lbl("대기 중");
    private readonly Label _lblStats = Lbl("");
    private readonly Button _btnStartStop = new() { Dock = DockStyle.Fill, Height = 44 };
    private readonly Panel _options = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };

    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _countdownTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _pickTimer = new() { Interval = 1000 };
    private readonly Queue<(long Ms, long Clicks)> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Keys _hotkeyKey;
    private Keys _hotkeyMods;
    private bool _capturingHotkey;
    private int _startCountdown;
    private int _pickCountdown;

    public MainForm()
    {
        _settings = SettingsStore.Load();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Malgun Gothic", 9F);
        Text = "AutoClicker";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        ApplySettingsToUi(_settings);
        WireEvents();
        ResumeLayout(true);

        ApplyTrigger();
        UpdateIntervalInfo();
        UpdateIdleUi(null);
    }

    // ---------------- 화면 배치 ----------------

    private void BuildLayout()
    {
        _lblWarn.ForeColor = Color.Firebrick;
        _lblStatus.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        _btnStartStop.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);

        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        foreach (int ms in new[] { 100, 50, 10, 5, 1 })
        {
            var b = new Button { Text = $"{ms}ms", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2), Tag = ms };
            b.Click += (_, _) => _nudInterval.Value = (int)b.Tag!;
            presets.Controls.Add(b);
        }

        var intervalGroup = Group("클릭 간격",
            Row(Lbl("간격"), _nudInterval, Lbl("ms"), _lblCps),
            presets,
            Row(Lbl("랜덤 흔들림 ±"), _nudJitter, Lbl("ms")),
            _lblWarn);

        var clickGroup = Group("클릭 옵션",
            Row(Lbl("버튼"), _cmbButton, Lbl("종류"), _cmbClick),
            Row(_rbCursor),
            Row(_rbFixed, Lbl("X"), _nudX, Lbl("Y"), _nudY, _btnPick));

        var repeatGroup = Group("반복 / 자동 정지",
            Row(_rbInfinite, _rbCount, _nudCount, Lbl("회")),
            Row(_nudMaxSec, Lbl("초 후 자동 정지 (0 = 제한 없음)")));

        var hotkeyGroup = Group("단축키",
            Row(_rbToggle, _rbHold),
            Row(Lbl("시작/정지 키"), _txtHotkey, Lbl("← 클릭해서 변경")),
            _lblHotkeyHint,
            Row(Lbl("비상 정지: Esc (클릭 중에만 동작)")),
            _chkTopMost);

        var optionStack = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        foreach (var g in new[] { intervalGroup, clickGroup, repeatGroup, hotkeyGroup })
            optionStack.Controls.Add(g);
        _options.Controls.Add(optionStack);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };
        root.Controls.Add(_options);
        root.Controls.Add(_lblStatus);
        root.Controls.Add(_lblStats);
        root.Controls.Add(_btnStartStop);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        _nudInterval.ValueChanged += (_, _) => UpdateIntervalInfo();
        // 두 라디오 버튼이 서로 다른 줄(컨테이너)에 있어서 자동으로 묶이지 않으므로 직접 연결한다.
        _rbCursor.CheckedChanged += (_, _) => { if (_rbCursor.Checked) _rbFixed.Checked = false; };
        _rbFixed.CheckedChanged += (_, _) => { if (_rbFixed.Checked) _rbCursor.Checked = false; UpdateEnabledStates(); };
        _rbCount.CheckedChanged += (_, _) => UpdateEnabledStates();
        _rbToggle.CheckedChanged += (_, _) => { if (_rbToggle.Checked) ApplyTrigger(); };
        _rbHold.CheckedChanged += (_, _) => { if (_rbHold.Checked) ApplyTrigger(); };
        _chkTopMost.CheckedChanged += (_, _) => TopMost = _chkTopMost.Checked;

        _txtHotkey.Click += (_, _) => BeginHotkeyCapture();
        _txtHotkey.KeyDown += OnHotkeyCaptureKeyDown;
        _txtHotkey.Leave += (_, _) => EndHotkeyCapture();

        _btnPick.Click += (_, _) => BeginPick();
        _pickTimer.Tick += (_, _) => { if (--_pickCountdown <= 0) CapturePosition(); else UpdatePickButton(); };

        _btnStartStop.Click += (_, _) => OnStartStopButton();
        _countdownTimer.Tick += (_, _) => { if (--_startCountdown <= 0) StartClicking(); else UpdateIdleUi(null); };
        _uiTimer.Tick += (_, _) => UpdateStats();

        _engine.StateChanged += OnEngineStateChanged;
        _hotkeys.TogglePressed += () => { if (_engine.IsRunning) _engine.Stop(); else StartClicking(); };
        _hotkeys.HoldDown += StartClicking;
        _hotkeys.HoldUp += () => _engine.Stop();
        _hotkeys.EmergencyPressed += () => { CancelCountdown(); _engine.Stop(StopReason.Emergency); };
        _hotkeys.CapturePressed += CapturePosition;
    }

    // ---------------- 시작 / 정지 ----------------

    private void OnStartStopButton()
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
        }
        else if (_countdownTimer.Enabled)
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
        _settings = ReadSettingsFromUi();
        _engine.Start(_settings);
    }

    private void CancelCountdown()
    {
        if (!_countdownTimer.Enabled)
            return;
        _countdownTimer.Stop();
        if (!_engine.IsRunning)
            UpdateIdleUi(null);
    }

    private void OnEngineStateChanged(bool running, StopReason? reason)
    {
        // 클릭 스레드에서 호출되므로 UI 스레드로 넘긴다.
        if (!IsHandleCreated || IsDisposed)
            return;
        BeginInvoke(() =>
        {
            if (running)
            {
                _hotkeys.SetEmergencyEnabled(true);
                _options.Enabled = false;
                _samples.Clear();
                _lblStatus.Text = "클릭 중...";
                _lblStatus.ForeColor = Color.SeaGreen;
                _btnStartStop.Text = $"정지  ({HotkeyText()} / Esc)";
                _uiTimer.Start();
            }
            else
            {
                _uiTimer.Stop();
                UpdateStats();
                _hotkeys.SetEmergencyEnabled(false);
                _options.Enabled = true;
                UpdateIdleUi(reason);
            }
        });
    }

    private void UpdateIdleUi(StopReason? reason)
    {
        if (_countdownTimer.Enabled)
        {
            _lblStatus.Text = $"{_startCountdown}초 후 시작 - 클릭할 곳에 커서를 두세요";
            _lblStatus.ForeColor = Color.DarkOrange;
            _btnStartStop.Text = "취소";
            return;
        }

        _lblStatus.Text = reason switch
        {
            StopReason.Emergency => "비상 정지됨 (Esc)",
            StopReason.CountReached => "설정한 횟수를 모두 클릭함",
            StopReason.DurationReached => "최대 시간이 지나 정지함",
            StopReason.Error => "오류로 정지함",
            StopReason.User => "정지됨",
            _ => "대기 중",
        };
        _lblStatus.ForeColor = reason is StopReason.Emergency or StopReason.Error ? Color.Firebrick : SystemColors.ControlText;
        _btnStartStop.Text = $"시작  ({HotkeyText()} 또는 {StartDelaySec}초 후)";
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
            text += $" · 전달 안 됨 {failed:N0}회 (관리자 권한 창?)";
        _lblStats.Text = text;
    }

    // ---------------- 단축키 ----------------

    private void ApplyTrigger()
    {
        var mode = _rbHold.Checked ? TriggerMode.Hold : TriggerMode.Toggle;
        bool ok = _hotkeys.SetTrigger(mode, _hotkeyKey, _hotkeyMods);
        _txtHotkey.Text = HotkeyText();
        _lblHotkeyHint.Text = "이 키 조합은 다른 프로그램이 쓰고 있어요. 다른 키를 골라주세요.";
        _lblHotkeyHint.Visible = !ok;
        _lblHotkeyHint.ForeColor = Color.Firebrick;
        if (!_engine.IsRunning)
            UpdateIdleUi(null);
    }

    private void BeginHotkeyCapture()
    {
        if (_engine.IsRunning)
            return;
        _capturingHotkey = true;
        _hotkeys.ClearTrigger(); // 지금 단축키가 입력을 가로채지 않도록 잠시 해제
        _txtHotkey.Text = "키 조합을 누르세요 (Esc 취소)";
    }

    private void OnHotkeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturingHotkey)
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
            return; // 수식키만 누른 상태면 나머지 키를 기다린다

        if (e.KeyCode != Keys.Escape)
        {
            _hotkeyKey = e.KeyCode;
            _hotkeyMods = e.Modifiers & (Keys.Control | Keys.Alt | Keys.Shift);
        }
        EndHotkeyCapture();
    }

    private void EndHotkeyCapture()
    {
        if (!_capturingHotkey)
            return;
        _capturingHotkey = false;
        ApplyTrigger();
        _btnStartStop.Focus();
    }

    private string HotkeyText() => ClickerSettings.FormatHotkey(_hotkeyKey, _hotkeyMods);

    // ---------------- 좌표 지정 ----------------

    private void BeginPick()
    {
        _pickCountdown = PickDelaySec;
        _hotkeys.SetCaptureEnabled(true);
        _pickTimer.Start();
        UpdatePickButton();
    }

    private void CapturePosition()
    {
        if (!_pickTimer.Enabled)
            return;
        _pickTimer.Stop();
        _hotkeys.SetCaptureEnabled(false);
        if (NativeMethods.GetCursorPos(out var p))
        {
            _nudX.Value = Math.Clamp(p.X, (int)_nudX.Minimum, (int)_nudX.Maximum);
            _nudY.Value = Math.Clamp(p.Y, (int)_nudY.Minimum, (int)_nudY.Maximum);
            _rbFixed.Checked = true;
        }
        UpdatePickButton();
    }

    private void UpdatePickButton() =>
        _btnPick.Text = _pickTimer.Enabled ? $"{_pickCountdown}초 뒤 지정 (F8 = 지금)" : "좌표 지정";

    // ---------------- 설정 <-> 화면 ----------------

    private void ApplySettingsToUi(ClickerSettings s)
    {
        _nudInterval.Value = s.IntervalMs;
        _nudJitter.Value = s.JitterMs;
        _cmbButton.SelectedIndex = (int)s.Button;
        _cmbClick.SelectedIndex = (int)s.Click;
        (s.Position == PositionMode.Fixed ? _rbFixed : _rbCursor).Checked = true;
        _nudX.Value = Math.Clamp(s.FixedX, (int)_nudX.Minimum, (int)_nudX.Maximum);
        _nudY.Value = Math.Clamp(s.FixedY, (int)_nudY.Minimum, (int)_nudY.Maximum);
        (s.RepeatCount > 0 ? _rbCount : _rbInfinite).Checked = true;
        _nudCount.Value = Math.Clamp(s.RepeatCount > 0 ? s.RepeatCount : 100, (long)_nudCount.Minimum, (long)_nudCount.Maximum);
        _nudMaxSec.Value = s.MaxDurationSec;
        (s.Trigger == TriggerMode.Hold ? _rbHold : _rbToggle).Checked = true;
        _hotkeyKey = s.HotkeyKey;
        _hotkeyMods = s.HotkeyModifiers;
        _chkTopMost.Checked = s.AlwaysOnTop;
        TopMost = s.AlwaysOnTop;
        UpdatePickButton();
        UpdateEnabledStates();
    }

    private ClickerSettings ReadSettingsFromUi() => new()
    {
        IntervalMs = (int)_nudInterval.Value,
        JitterMs = (int)_nudJitter.Value,
        Button = (MouseButtonKind)_cmbButton.SelectedIndex,
        Click = (ClickKind)_cmbClick.SelectedIndex,
        Position = _rbFixed.Checked ? PositionMode.Fixed : PositionMode.Cursor,
        FixedX = (int)_nudX.Value,
        FixedY = (int)_nudY.Value,
        RepeatCount = _rbCount.Checked ? (long)_nudCount.Value : 0,
        MaxDurationSec = (int)_nudMaxSec.Value,
        Trigger = _rbHold.Checked ? TriggerMode.Hold : TriggerMode.Toggle,
        HotkeyKey = _hotkeyKey,
        HotkeyModifiers = _hotkeyMods,
        AlwaysOnTop = _chkTopMost.Checked,
    };

    private void UpdateIntervalInfo()
    {
        int ms = (int)_nudInterval.Value;
        _lblCps.Text = $"≈ 초당 {1000.0 / ms:0.#}회";
        _lblWarn.Visible = ms < 5;
    }

    private void UpdateEnabledStates()
    {
        _nudX.Enabled = _nudY.Enabled = _rbFixed.Checked;
        _nudCount.Enabled = _rbCount.Checked;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.Dispose();
        _hotkeys.Dispose();
        _uiTimer.Dispose();
        _countdownTimer.Dispose();
        _pickTimer.Dispose();
        SettingsStore.Save(ReadSettingsFromUi());
        base.OnFormClosing(e);
    }

    // ---------------- 컨트롤 생성 도우미 ----------------

    private static Label Lbl(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };

    private static NumericUpDown Nud(decimal min, decimal max, int width) =>
        new() { Minimum = min, Maximum = max, Width = width, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };

    private static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        c.Items.AddRange(items);
        return c;
    }

    private static RadioButton Radio(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 0) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        p.Controls.AddRange(controls);
        return p;
    }

    private static GroupBox Group(string title, params Control[] rows)
    {
        var inner = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        inner.Controls.AddRange(rows);

        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
            Margin = new Padding(0, 0, 0, 8),
        };
        group.Controls.Add(inner);
        return group;
    }
}
