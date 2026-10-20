using AutoClickerMac.Core;

namespace AutoClickerMac.Platform;

/// <summary>OS마다 다른 부분(클릭 주입, 정밀 대기, 커서, 권한, 단축키)을 감싼다.</summary>
internal interface IPlatform : IDisposable
{
    /// <summary>맥이 아니어서 실제로 클릭하지 않는 미리보기 모드인지.</summary>
    bool IsPreview { get; }

    IHotkeySource Hotkeys { get; }

    /// <summary>손쉬운 사용 권한이 있는지 (없으면 클릭이 전달되지 않는다).</summary>
    bool IsAccessibilityTrusted();

    /// <summary>입력 모니터링 권한이 있는지 (없으면 단축키가 동작하지 않는다).</summary>
    bool IsInputMonitoringGranted();

    /// <summary>맥에 권한을 요청한다. 시스템 설정 목록에 앱이 등록되고, 처음엔 안내 창이 뜬다.</summary>
    void RequestPermissions();

    void OpenPrivacySettings(bool inputMonitoring);

    /// <summary>누름+뗌(더블이면 두 번)을 보낸다. 고정 좌표가 있으면 그 위치로 커서를 옮긴 뒤 클릭한다.</summary>
    bool Click(MouseButtonKind button, bool doubleClick, (int X, int Y)? fixedPosition);

    (int X, int Y) GetCursorPosition();

    /// <summary><paramref name="duration"/> 동안 잠든다. 그 사이 <paramref name="cancel"/>이 신호되면 false.</summary>
    bool Wait(TimeSpan duration, WaitHandle cancel);
}

/// <summary>전역 단축키. 이벤트는 백그라운드 스레드에서 발생할 수 있다.</summary>
internal interface IHotkeySource
{
    event Action? TogglePressed;
    event Action? HoldDown;
    event Action? HoldUp;
    event Action? EmergencyPressed;
    event Action? CapturePressed;

    /// <summary>키 감지를 시작한다. 권한이 없어 실패하면 false (나중에 다시 호출 가능).</summary>
    bool Start();

    void Configure(TriggerMode mode, int keyCode, MacModifiers modifiers);

    /// <summary>클릭 중에만 Esc 비상 정지를 받는다.</summary>
    bool EmergencyEnabled { get; set; }

    /// <summary>좌표 지정 대기 중에만 F8을 받는다.</summary>
    bool CaptureEnabled { get; set; }

    /// <summary>다음에 누르는 키 조합을 받아 콜백한다. Esc를 누르면 null로 취소된다.</summary>
    void CaptureNextKey(Action<(int KeyCode, MacModifiers Modifiers)?> onCaptured);
}
