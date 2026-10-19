# AutoClicker

Windows용 오토클릭입니다. 클릭 간격을 1ms 단위로 직접 정할 수 있고, 빠르게 돌려도 CPU를 거의 쓰지 않도록 만들었습니다.

## 기능
- **클릭 간격**: 1 ~ 60,000ms (기본 10ms = 초당 약 100회), 프리셋 100/50/10/5/1ms
- **랜덤 흔들림**: 간격에 ± ms 무작위 편차 추가
- **버튼 / 종류**: 왼쪽·오른쪽·가운데, 싱글·더블 클릭
- **위치**: 현재 커서 위치 또는 고정 좌표 (`좌표 지정`을 누르고 3초 안에 커서를 옮기거나 `F8`)
- **자동 정지**: N회 클릭 후, 또는 N초 후
- **단축키**: 기본 `F6`, 클릭해서 원하는 조합으로 변경
  - 누를 때마다 켜고 끄기 / 누르고 있는 동안만 클릭
- **비상 정지**: `Esc` (클릭 중에만 동작)
- 실측 CPS와 총 클릭 수 표시, 설정 자동 저장 (`%AppData%\AutoClicker\settings.json`)

## 동작 원리
| 부분 | 사용하는 것 |
|---|---|
| 클릭 주입 | `SendInput`: 누름+뗌을 한 번의 호출로 전송 |
| 대기 | 고해상도 대기 타이머(`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`). 바쁜 대기를 하지 않음 |
| 일정한 간격 | 절대 시각 기준으로 다음 클릭을 예약해서 오차가 쌓이지 않음 |
| 백그라운드 정밀도 | `SetProcessInformation`으로 Windows 11의 전원 스로틀링 해제 |
| 단축키 | `RegisterHotKey`(켜고 끄기), 저수준 키보드 훅(누르고 있는 동안) |

## 빌드 / 실행
.NET 10 SDK가 필요합니다.

```bash
dotnet run --project AutoClicker
```

## 배포용 exe 만들기
.NET이 설치되지 않은 PC에서도 실행되는 단일 exe를 만듭니다.

```bash
dotnet publish AutoClicker -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

`publish/AutoClicker.exe` 하나만 배포하면 됩니다.

## 측정 페이지
`tools/click-test.html`을 브라우저로 열고 클릭 영역 위에 커서를 둔 채 실행하면 실측 CPS와 클릭 간격(평균/최소/최대)을 볼 수 있습니다.

## 주의
- 관리자 권한으로 실행된 창에는 클릭이 전달되지 않습니다 (Windows 보안 정책). 화면에 "전달 안 됨"으로 표시됩니다.
- 코드 서명을 하지 않아서 처음 실행할 때 SmartScreen 경고가 뜰 수 있고, 백신이 오탐할 수 있습니다.
- 온라인 게임이나 예매 사이트는 대부분 약관으로 매크로를 금지합니다.
