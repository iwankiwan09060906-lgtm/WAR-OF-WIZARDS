# 전시 시연 실행 가이드 (처음 받는 사람용)

GitHub 에서 받은 뒤 **PC 1대 = 서버**, **Quest 최대 6대 = 플레이어** 로 시연을 여는 순서. 규칙 · 구현은 [Battle/Demo.md](Battle/Demo.md).

```text
[PC]  StartServer.bat ──▶ SpellboundServer.exe -server -demo  (AI 6명 경기가 늘 돌아감, 창 = 옵저버)
          │  Photon Cloud (지역 kr, 세션 "spellbound-dev-demo")
[Quest] 앱 실행 ──▶ 연습 3스킬 ──▶ 서버 접속 ──▶ "3초 뒤 입장" ──▶ AI 자리를 넘겨받아 플레이 ──▶ 쓰러지면 GAME OVER ──▶ 다시 연습부터
```

PC 와 Quest 는 **같은 Wi-Fi 일 필요가 없다** — 둘 다 인터넷만 되면 Photon Cloud 로 만난다. 백엔드(로그인 · 웹)는 시연에 필요 없다.

## 0. 준비물

| 무엇 | 비고 |
| --- | --- |
| Windows PC | 서버 + 빌드용 |
| Unity **6000.3.10f1** | Unity Hub 에서 설치할 때 **Android Build Support** (OpenJDK · SDK · NDK 포함) 와 **Windows Build Support (Mono)** 모듈을 같이 |
| Git + **Git LFS** | 모델 · 텍스처가 LFS 로 올라가 있다. LFS 없이 받으면 에셋이 깨진다 |
| Meta Quest 2 / 3 | **개발자 모드** 켜기 (휴대폰 Meta Horizon 앱 → 기기 → 개발자 모드), USB-C 데이터 케이블 |
| 인터넷 | PC · Quest 모두 (Photon Cloud) |

## 1. 받기

```bash
git lfs install
git clone <저장소 주소>
cd WAR-OF-WIZARDS
git checkout feat/polygonArena      # 시연 브랜치 (main 에 합쳤으면 생략)
```

Unity Hub → **Add** → `WAR-OF-WIZARDS/UnityClient` 폴더 → 6000.3.10f1 로 연다. 처음 열 때 임포트에 몇 분 걸린다.
씬은 `Assets/Scenes/Client/04_Battle.unity` 하나다.

## 2. PC 에서 먼저 확인 (헤드셋 · 서버 없이)

에디터에서 **Play** → 연습 모드 → 끝나면 서버 없이 이 PC 안에서 6인 시연 경기 (1번 = 나, 나머지 AI).

| 키 | 동작 |
| --- | --- |
| 마우스 왼쪽 드래그 / 숫자 1~4 | 룬 그리기 / 그 칸 스킬 바로 고르기 |
| F | 준비 (주먹) |
| G → 마우스 | 조준 (검지), 3초 유지하면 발사 |
| 마우스로 스킬 카드 클릭 | 따라 그릴 실루엣 보기 |
| A / D | 왼쪽 · 오른쪽 칸 이동 |
| N | 연습 건너뛰기 |

에디터에서도 서버에 붙여 보려면 `[Spellbound]` 오브젝트의 GameBootstrap → **Demo Offline In Editor** 끄기.

## 3. 서버 빌드 · 실행 (PC)

1. Unity 메뉴 **Spellbound → Build → PC Server (Windows)** → `Builds/Server/SpellboundServer.exe` (빌드 결과는 git 에 올라가지 않는다 — 각자 빌드).
2. 저장소 맨 위 **`StartServer.bat`** 더블클릭 → 서버 창이 뜨고 AI 6명 경기가 바로 시작된다. 꺼지면 3초 뒤 저절로 다시 켠다. 로그 `Builds/Server/server_demo.log`.
3. 서버 창 = 옵저버: **1~6** 그 플레이어 시점 · **Tab** 다음 · **O** 자동 순환 켜기/끄기 (8초마다) · **V** 전체 보기 · 휠 확대.
4. (선택) PC 를 켤 때 서버가 저절로 켜지게: `ServerAutostart.bat` 한 번 실행 (다시 실행하면 끔).

## 4. Quest 빌드 · 설치

1. Quest 를 USB 로 PC 에 연결 → 헤드셋 안에서 **USB 디버깅 허용**.
2. Unity 메뉴 **Spellbound → Build → Quest APK (Fusion Client → PC server)** → `Builds/Quest/Spellbound.apk`
   (서버(Windows) ↔ Quest(Android) 를 번갈아 빌드하면 플랫폼 전환에 시간이 오래 걸린다 — 한쪽씩 몰아서).
3. 설치 (둘 중 하나):
   - **Meta Quest Developer Hub** → 기기 → Apps → APK 끌어다 놓기
   - 명령줄: `"C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r Builds\Quest\Spellbound.apk`
4. Quest 설정 → 움직임 추적 → **손 추적 켜기**. 컨트롤러는 내려놓는다 (손으로 플레이).
5. 앱 실행: 라이브러리 → **알 수 없는 출처** → **UnityClient**.

**서버 빌드와 Quest 빌드는 같은 커밋에서 만들어야 한다** (서버 ↔ Quest 가 주고받는 상태 모양이 같아야 접속된다).

## 5. 시연 진행

- 서버를 먼저 켠다 (`StartServer.bat`). 사람이 없어도 AI 6명 경기가 계속 돈다.
- 관람객이 Quest 를 쓰고 앱을 켜면: 연습(스킬 3개 받아쓰기) → 서버 접속 → 방 안 모두에게 "3초 뒤 새로운 플레이어가 N번 자리에" → AI 자리를 이어받는다. 최대 6명, 7번째부터는 자리가 날 때까지 대기.
- 쓰러지면 **GAME OVER** 화면 (8초) → 그 자리는 AI 가 잇고, 앱은 연습부터 다시 (다음 관람객 차례).
- 경기 중 서버와 연결이 끊기면 2초 뒤 앱이 알아서 다시 접속한다 (연습 없이 바로, 빈 AI 자리를 다시 넘겨받음 — 끊긴 동안 내 자리는 AI 가 잇는다).
- 헤드셋을 20초 넘게 벗었다가 다시 쓰면 다음 관람객으로 보고 연습부터 다시 시작한다 (자리는 AI 에게).
- 플레이 중 눈 아래 스킬창을 내려다보면 검지 끝을 따라 포인터가 뜬다 — 카드를 가리키고 핀치하면 그 스킬의 실루엣이 눈앞에 뜬다 (그대로 따라 그린다). 10초 동안 아무것도 안 하면 카드 하나의 실루엣이 저절로 뜬다.

## 6. 여러 팀이 동시에 시연할 때

Photon AppId 가 저장소에 들어 있어 **같은 세션 이름으로 서버를 두 대 켜면 충돌**한다 (Quest 가 남의 서버에 붙음).
팀마다 다른 이름을 쓴다: `04_Battle` 씬 → `[Spellbound]` → GameBootstrap → **Session Name** 을 바꾸고 (예: `spellbound-teamA`) **서버와 Quest 를 둘 다 다시 빌드**한다.

## 7. 안 될 때

| 증상 | 확인 |
| --- | --- |
| Quest 에 "Server not found - retrying" | 서버 창이 켜져 있는지 · PC / Quest 인터넷 · 서버와 Quest 가 같은 커밋 · 같은 Session Name 으로 빌드했는지. 서버가 나중에 켜져도 Quest 는 계속 다시 찾는다 |
| 빌드가 30분 넘게 걸림 (로그에 `Universal Render Pipeline/Lit` · `~36238M prepared`) | 이 프로젝트는 Built-in 렌더러다. `ProjectSettings/GraphicsSettings.asset` 의 `m_RenderPipelineGlobalSettingsMap` 에 URP 전역 설정이 다시 등록되면 쓰지도 않는 URP Lit 셰이더 변형 362억 개를 훑느라 30분이 걸린다 → `{}` 로 비우고 `Assets/UniversalRenderPipelineGlobalSettings.asset` 을 지운다 |
| Play 를 누르면 "All compiler errors have to be fixed" | Unity Console 의 첫 빨간 줄. LFS 없이 받았으면 `git lfs pull` |
| 에셋이 분홍색 · 모델이 없음 | `git lfs pull` 후 Unity 재시작 |
| 손이 안 잡힘 | Quest 손 추적 켜기, 컨트롤러 내려놓기, 밝은 곳 |
| 서버 창에 플레이어가 안 들어옴 | `Builds/Server/server_demo.log` 의 `[FusionServerLauncher]` · `[NetworkMatchState]` 줄 |

## 8. 테스트 (Unity 없이)

```text
cd Tools/BotLab
dotnet run -c Release -- demotest    # 시연 규칙 전체 (.NET 9 SDK, 없으면 Unity 에 들어 있는 SDK 8 로 TargetFramework 를 net8.0 으로 바꿔 실행)
```
