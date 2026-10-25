# Spellbound VR — 사람이 직접 해야 하는 일

> 대상: 4인 팀 (ㅊㄱㅇ · ㅈㅈㅇ · ㅈㅇㅈ · ㅇㅎㅅ)
> 기준: `Spellbound_VR_Master_Prompt_v6.0.md`, 작업일 2026-10-03 · 갱신 2026-10-24 (스킬 개편 · 에셋 정리)

---

## 0. 현재 상태 한눈에

| 항목 | 상태 |
| --- | --- |
| 코드 컴파일 | ✅ Unity 6000.3.10f1 실제 에디터에서 에러 0 (Player/Editor 어셈블리, Fusion 위버 포함) |
| 에디터 셋업 | ✅ `Spellbound > 1. Setup Battle Scene` 실행 완료 — 데이터 에셋 14개, 네트워크 프리팹 2개, 04_Battle 씬 연결 · 저장 |
| 로컬 봇전 (에디터 Play) | ✅ 실행 확인 — 카운트다운 → 웨이브 · 미니언 · 타워 교전 → 봇 시전 → AFK 기권 → 자동 재시작 3경기 연속, 예외 0 |
| 시전 흐름 | ✅ 룬 그리기 → $P+ 인식(S03 · S07 · S08 모두 정답) → 주먹 → 포인팅 → 2초 조준 → 서버 확정, 쿨타임 중 재시전 거절, 손바닥 취소, 좌우 이동 |
| PC 전용 서버 (Fusion Server) | ✅ 에디터에서 기동 확인 — Photon 클라우드 `spellbound-dev` 세션 생성, NetworkMatchState 스폰 |
| Quest 실기 · Quest↔PC 접속 | ⬜ **사람이 해야 함** (헤드셋 필요, 아래 2 · 3장) |

구현 범위: 핵심 데이터 모델 · 서버 권한 시뮬레이션(메인 루프) · 입력/제스처/조준 · **스킬 19종 전부 (Executor 7종)** · 기본 봇 · 간이 HUD · Fusion 서버/클라이언트 · 웹 백엔드 연결.
스킬별 동작 · 수치는 마스터 프롬프트 §14.3 · §14.4가 기준이다 (코드 기본값 `SpellDefinition.CreateDefault`와 같다).

---

## 0-1. VR로 전체 흐름 테스트 (Quest) — [`Docs/GameDesign/VrTestGuide.md`](Docs/GameDesign/VrTestGuide.md)

`Backend/run_server.bat` → Unity `Spellbound > Build > ★ Quest APK — VR 전체 흐름` → `adb install -r Builds/Quest/WarOfWizards_VR.apk` → 헤드셋에서 게스트 로그인 → 봇 연습전 → 덱 선택 → 경기 → 결과(전적 저장) → 로비 / 게임 종료.
빌드한 PC의 LAN 주소가 앱에 자동으로 들어간다 (같은 Wi-Fi + `Backend/open-firewall.bat`). 경기 중 메뉴(기권) = 왼손 메뉴 제스처 / 컨트롤러 ☰.

- 2026-10-25: 씬 `04_Battle`의 `GameBootstrap.bootMode`가 `FusionClient`로 저장되어 있어(문서상 기본은 LocalVsBot) PC 게임 서버 없이 Play하면 카운트다운에서 멈췄다 → **`Auto`(에디터 = LocalVsBot)로 되돌림**. 빌드 메뉴는 빌드마다 모드를 직접 정하므로 영향 없다.

## 1. 지금 바로 해볼 것 (헤드셋 없이, 5분)

1. Unity에서 `Assets/Scenes/Client/04_Battle.unity`를 열고 **Play**를 누른다.
2. Game 뷰를 한 번 클릭해 포커스를 준다 (포커스가 없으면 Input System이 키보드를 무시한다).
3. 조작 (자세한 표: `Docs/Bot/ManualControlKeys.md`)
   - `1`~`8` 덱 슬롯 바로 시전 (씬 기본 덱: S03 · S07 · S08 · S01 · S11 · S13 · H01 · H04 — 조준형은 마우스 위치로 2초 조준 후 시전)
   - 마우스 왼쪽 드래그로 룬 그리기 (모양: `Assets/Art/Runes/Designs/` 19종) → `F`(주먹 = 그리기 끝 · 인식) → `G`(포인팅). 인식된 룬 아이콘은 마우스 커서(손 모드는 검지 끝)를 따라다닌다
     (`Recognize All Runes`가 켜져 있으면 덱 밖 룬도 로컬 봇전에서 쿨타임 없이 테스트 시전된다)
     (여러 획 룬: 드래그를 여러 번 한 뒤 `F`. 획 사이 5초 무입력이면 취소)
   - `A` / `D` 이동, `X` 또는 우클릭 취소
   - `F1`~`F8` 테스트 시나리오, `Alt`+키 봇 수동 조종
4. Game 뷰 포커스 없이 재현하려면 메뉴 `Spellbound > Debug > ...` (룬 그리기 · 주먹 · 포인팅 · 조준 고정 · 이동).
5. 30초 동안 아무것도 안 하면 규칙(§10)대로 **AFK 기권패**한다. 개발 중 불편하면 `[Spellbound]` 오브젝트의 `GameBootstrap > Local Afk Enabled`를 끈다.

---

## 2. Quest 3 실기 (ㅊㄱㅇ) — 필수

| # | 할 일 | 비고 |
| --- | --- | --- |
| 2-1 | Quest 개발자 모드 · 설정 > 손 추적(핸드트래킹) ON | |
| 2-2 | 메뉴 `Spellbound > Build > Quest APK (Offline vs Bot)` → `Builds/Quest/Spellbound_Offline.apk` 설치 (MQDH 또는 `adb install -r`) | 플랫폼이 Android가 아니면 자동 전환 |
| 2-3 | 실기에서 **손 자세 임계값 튜닝** (W1 실기 검증 항목) | 아래 표 |
| 2-4 | 실제 손으로 그린 룬 궤적으로 **템플릿 · 인식 임계값 튜닝** | 아래 2.2 |
| 2-5 | HUD 위치 · 크기 확인 (`UI/C12_HUD/BattleHudView.cs`의 `localPosition / tiltDegrees / worldScale` 초기값) | 현재: 머리 기준 아래 0.55m · 앞 1.35m |
| 2-6 | 이동 비네팅 강도 확인 (`Presentation/SnapVignette.cs`의 `maxAlpha / duration` 초기값) — 멀미 여부 | 완전 암전 아님 |

### 2.1 튜닝할 값

대부분 **Play 시 코드가 생성하는 컴포넌트**라 씬 인스펙터에 없다 → **파일의 필드 초기값을 고치고 APK를 다시 빌드**한다.
씬 인스펙터에서 바로 바꿀 수 있는 것은 `HandJointProvider`(손 프리팹에 붙어 있음)뿐이다.

| 파일 (`Assets/_Project/`) | 필드 | 기본값 | 의미 |
| --- | --- | --- | --- |
| `XR/HandPoseDetector.cs` | `ExtendedMaxDeg` / `CurledMinDeg` / `HoldSeconds` | 55° / 95° / 0.08s | 손가락 펴짐 · 굽힘 판정 각도 |
| `XR/PinchDetector.cs` | `StartStrength` / `EndStrength` | 0.85 / 0.45 | 핀치 시작 · 종료 (히스테리시스) |
| `Input/LeftHand/MoveGestureDetector.cs` | `MinDistance` / `MinPeakSpeed` / `CooldownSeconds` | 0.14m / 0.9m/s / 0.5s | 왼손 휘두르기 이동 |
| `Input/HandTrackingInput.cs` | `aimSmoothing` / `useIndexFingerRay` | 0.6 / true | 검지 조준 레이 (false면 OVR 시스템 포인터) |
| 씬 `OVRHandPrefab_Left/Right` > `HandJointProvider` | `lostGraceSeconds` | 0.2s | 트래킹 깜빡임 유예 (넘으면 시전 취소) — 인스펙터 수정 가능 |

### 2.2 룬 인식 튜닝

- 현재 템플릿은 **디자인 19종을 옮긴 절차 생성 도형**이다 (`Gesture/Runtime/RuneDesigns.cs` → `Assets/_Project/Gesture/Templates/Data/Rune_*.asset`, 메뉴 `Spellbound > Runes > Build Rune Templates`).
- **인식기 v2 (2026-10-25)**: $P+(64점) + 획 방향 · 획 수 + 룬마다 사람 손처럼 흔든 변형 20개 (`Gesture/Recognizer/RuneRecognizer.cs`, `RuneAugmenter.cs`). 벤치마크(19종 전부 후보, 강한 노이즈): 정답 93.4% → 98.5%, 잘못 시전 1.97% → 0.26%. S07 ↔ H02 ↔ S09 혼동이 사라졌다. 그래도 헷갈리는 룬은 실제 손 궤적을 녹화해 넣는다 (아래).
- 덱 밖 룬 테스트: `GameBootstrap > Recognize All Runes`(기본 켜짐) — 시전 요청 대신 `[RuneTest]` 로그. 정식 플레이에서는 끈다.
- 인식할 때마다 콘솔에 `[CastStateMachine] 룬 인식 → S03 점수 0.071 2위 S08 0.164 (비율 2.31) → None` 형식으로 찍힌다. 애매하면 시전하지 않고 HUD에 "Rune unclear: S07 or H02? Draw again".
- 판정 임계값: `Gesture/Runtime/GestureValidator.cs`의 `GestureValidationSettings` — `MaxAcceptScore 0.17`(정상 룬은 보통 0.06~0.15, 낙서는 0.19 이상), `MinScoreRatio 1.08`. 너무 자주 "not recognized"면 0.17 → 0.2로 올리고, 잘못 시전되면 비율을 1.12로 올린다.
- Quest에서 인식 로그 보기: PC에 USB 연결 후 `adb logcat -s Unity | findstr CastStateMachine` (Development 빌드라 로그가 나온다).
- 실제 손 궤적을 샘플로 추가하려면 `RuneTemplate.AddSample(label, points)`를 쓰거나 인스펙터에서 `Samples`에 직접 점을 넣는다. (녹화 UI는 아직 없음 — `GestureRecorder.LastStroke`에 마지막 궤적이 남는다.)
- **ㅇㅎㅅ와 19종 룬 디자인 확정 시 유사도 검증 필수 (§9).** 원(○)과 U자, 삼각형과 ^(캐럿)처럼 닮은 쌍은 피한다.

---

## 3. PC 서버 ↔ Quest 접속 (ㅈㅈㅇ) — 필수

| # | 할 일 |
| --- | --- |
| 3-1 | 메뉴 `Spellbound > Build > PC Server (Windows)` → `Builds/Server/SpellboundServer.exe` + `RunServer.bat` 생성 |
| 3-2 | `RunServer.bat` 실행 (창 모드, `-server`). Windows 방화벽 허용 팝업이 뜨면 허용 |
| 3-3 | 메뉴 `Spellbound > Build > Quest APK (Fusion Client → PC server)` → `Builds/Quest/Spellbound.apk` 설치 |
| 3-4 | Quest 실행 → 서버 디버그 패널에 `HUMAN` 입장 확인 → 10초(`MatchRuleConfig.BotFillWaitSeconds`) 안에 두 번째 사람이 없으면 **봇 자동 매칭** |
| 3-5 | 사람 대 사람: 에디터를 `Spellbound > Mode > Fusion Client`로 바꿔 Play → 두 번째 플레이어로 입장 |
| 3-6 | 10분 연속 경기 · 회피 판정 · 핑(`DebugOverlay`, F11) 기록 → §21.3 지연 보정 여부 결정 (W6) |

- Photon 설정: `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` (AppId 설정됨, 지역 `kr`). 서버와 클라이언트는 **같은 AppId · 지역 · 세션 이름**(`GameBootstrap.sessionName = spellbound-dev`)이어야 한다.
- 명령줄: `SpellboundServer.exe -server -session 이름` / 클라이언트 `-client` (bootMode가 `Auto`일 때).
- 무료 플랜 20 CCU 안에서 서버 접속도 CCU에 포함되는지 확인 (§3.2, W1 항목).
- 빌드 메뉴는 빌드 직전에 `GameBootstrap.bootMode`를 바꾸고 빌드 후 원래 값으로 되돌린다. 에디터 기본값은 `LocalVsBot`.
- **서버 창 카메라** (`Server/ServerSpectatorCamera.cs`): 대기 · 카운트다운은 Home 뒤 시점, **경기가 시작되면 위에서 약 42° 내려다보는 사이드뷰**로 자동 전환 (왼쪽 Home · 오른쪽 Away, 전장 전체가 가로로 들어오게 거리 자동). `V` = 사이드뷰 → 탑뷰 → Home 시점, `B` = 반대편 옆에서 보기, 마우스 휠 = 확대 · 축소. 봇 조종 `Q/W/E`는 시점과 상관없이 **Home 기준** 좌 / 중 / 우.
- **경기 전 화면(C01~C11)은 Fusion이 아니라 웹 백엔드(`Backend/wow_server.py`, HTTP 8080)에 붙는다.** 전투(Fusion UDP)는 되는데 로그인 · 로비만 "서버 꺼짐"이면: ① Player Settings > Other > **Allow downloads over HTTP = Always allowed** (2026-10-25에 켬 — 꺼져 있으면 `http://IP` 요청이 막혀 화면이 "확인 중…"에 멈췄다) ② C01 **주소 변경**에 서버 PC IP ③ `Backend/open-firewall.bat` (8080 허용) ④ 같은 Wi-Fi.

---

## 4. 씬 · 에셋 정리 (ㅊㄱㅇ / ㅇㅎㅅ)

| # | 할 일 | 이유 |
| --- | --- | --- |
| 4-1 | ✅ 완료 — `Lane_Left`(x=-5) / `Lane_Right`(x=+5)로 이름 교체 | 플레이어는 +Z를 보므로 x=+5가 **오른쪽**이다. 코드는 이름을 쓰지 않아 동작엔 영향 없음 |
| 4-2 | ✅ 비활성화 — 루트 `NetworkPrefabs`(씬 NetworkObject) | 용도 없음. 확인 후 삭제해도 된다 |
| 4-3 | ✅ 비활성화 — 루트 `Minion` 테스트 캡슐 | 실제 미니언은 코드가 풀로 생성한다. 확인 후 삭제하거나 `MDL_Minion_Melee` 원본으로 사용 |
| 4-4 | ✅ 완료 — `Spellbound > 5. Build Placeholder Arena`로 Home/Away 발판 · 타워 · 넥서스 · 레인 · 바닥을 임시 배치하고 `[ArenaLayout]`에 연결 | 발판 이름 `Home_Commander_L/C/R`, `Away_Commander_L/C/R`. 눈높이 5m |
| 4-5 | ✅ `Arena/Home/Home_Nexus`(z=5) · `Home_Tower`(z=8), 모양은 각 `Body` 자식 | §5: 발판 → 넥서스 → 타워 순 |
| 4-6 | 아트 투입 → **`Docs/Art/AssetIntegrationGuide.md`** (파일명 · 폴더 · 피벗 · 등록 메뉴) | 이름만 맞으면 코드 수정 없이 자동 연결 |

## 5. 계약서(Contracts) 변경 — 4인 합의 필요 (§25.1)

구현하면서 초안의 TODO를 채웠다. **전원 동의 후 ㅈㅈㅇ가 확정**해야 한다.

| 파일 | 변경 |
| --- | --- |
| `Contracts/IBotBrain.cs` | `BotObservation`에 `SelfSlotReadyMask`, 양측 타워/넥서스 생존, 날아오는 투사체 수 · 열 · ETA 추가. TODO 주석 제거 |
| `Contracts/IDamageResolver.cs` | `DamageContext`에 `AttackerId`, `IsDodgeable`, `AimedWorldColumn` 추가 (§12.5 ③ 회피 판정을 리졸버 안에서 하기 위해) |

추가로 확정할 규칙 (코드는 아래처럼 동작한다):
- **열 좌표**: 요청(`SpellCastRequest.TargetColumn`, `MoveRequest.Direction`)과 게임 이벤트의 열은 **그 플레이어 자신의 시점**. Away는 좌우가 반대다. 서버 내부는 "월드 열(Home 시점)".
- `OnBreachStarted(team)`의 team = **돌파를 시작한 쪽**(공격 측).
- `OnSlotCooldownStarted(slot, duration)`의 duration = 받은 시점의 **남은** 쿨타임.
- 이벤트 발행자에 계약 외 확장 이벤트가 있다: `OnPhaseInfo`(카운트다운), `OnLocalCastAccepted`, `OnLocalMoveRejected`, `OnHitFeedback`(데미지 텍스트). 계약에 넣을지 결정.

---

## 6. 수치 밸런싱 (§3.3 "추후 설정")

확정 수치(쿨타임 5/20초, +20%/+40%, 20%, 10초, 5분, 30초, 1초, 웨이브 10초 등)는 기본값으로 넣었다. 나머지는 **임시값**이며 에셋에서 조정한다.

| 에셋 | 주요 임시값 |
| --- | --- |
| `_Project/Core/Data/MatchRuleConfig.asset` | 플레이어 HP 1000, 타워 3000 / 넥서스 4000 (2026-10-25 2배), 타워 공격 35, 넥서스 회복 25, 강화당 공격 +15% · 쿨타임 -10%(하한 50%), 봇 매칭 대기 10초, 재시작 10초 |
| `_Project/Spells/Definition/Data/Spell_*.asset` (19종) | 마스터 프롬프트 §14.3 수치표 그대로 (2026-10-24: 고위력 6종 강화, S05 · S06 · H03 · H05 동작 변경) |
| `_Project/Combat/Minion/Data/Minion_*.asset` | 근접 180HP/14, 원거리 120HP/11, Brute 630HP/30 · 구조물 ×2, 오크 1100HP/65, 드래곤 450HP/45, 터렛 900HP/55, 소환탑 1400HP |
| `_Project/Bot/Data/BotConfig.asset` | 시전 간격 4~8초 |

- 스킬 수치를 코드 기본값으로 되돌리려면 `Spellbound > Skills > Rebuild Spell Definitions (19)` (에셋을 덮어쓴다).

- `MatchRuleConfig.AllowPartialDeck = true` — 19종이 완성되기 전까지 **8장 미만 덱(빈 슬롯)**을 허용한다. 19종 완성 후 false로.
- 로컬 테스트 덱은 `GameBootstrap.localDeck`(씬 값: S03 · S07 · S08 · S01 · S11 · S13 · H01 · H04)에서 온다. 백엔드 로그인 시에는 웹 마법서 덱을 쓴다.

---

## 7. 스킬 추가 · 수정 방법 (ㅊㄱㅇ, Rule 11)

1. 수치 · 조준 방식은 `SpellDefinition.CreateDefault`(코드 기본값)와 `Spell_*.asset`을 같이 고친다 — 에셋이 게임에서 실제로 쓰는 값이다.
2. `Executor` · `Behavior` 지정. 이미 있는 조합이면 코드 없이 끝난다. Executor 7종(Projectile · Area · Beam · Summon · Structure · Buff · Global)은 모두 구현돼 있다.
3. 새 동작이면 `Spells/Behaviors/`에 Behavior 추가 → 해당 Executor의 `switch`에 연결. 조준 표시 모양(직사각형 범위 등)은 `Input/RightHand/AimPointer.cs`의 `Begin`.
4. 상태 효과는 `Spells/Effects/`에 `IStatusEffect` 구현 (예: `ShieldRegenEffect`).
5. 룬 템플릿 `Rune_Sxx_*.asset` → `RuneTemplateLibrary.asset`에 추가.
6. 데미지는 반드시 `world.Damage.ResolveDamage(...)` (HP 직접 변경 금지, Rule 8).
7. 테스트: 에디터 로컬 모드 또는 PC 서버에서 `Alt+Space`(수동) → `Alt+슬롯번호`로 봇이 시전하게 해 재현.

---

## 8. 설계상 선택 · 알려진 제약

- **미니언 이동은 NavMesh 대신 아레나 평면(u, v) 직선 이동**이다 (서버 경량 · 할당 0). 장애물 회피가 필요해지면 `MinionAI` 이동부만 NavMesh 경로로 교체한다. 씬의 NavMesh는 그대로 두었다.
- **씬은 04_Battle 하나**로 클라이언트 · 서버 모두 동작한다 (`GameBootstrap.bootMode`로 분기). §27의 `S00/S01` 서버 씬 분리가 필요하면 04_Battle을 복제하고 모드를 `DedicatedServer`로 저장하면 된다.
- 서버 빌드에도 Meta XR 패키지 코드가 포함된다 (Windows Standalone엔 XR 로더가 없어 실행되지 않음). 완전 분리(§36-5)는 asmdef 분리 작업이 필요하다.
- **Addressables**: MCP 패키지 의존성으로 설치돼 있어 Fusion이 Addressables를 쓰려다 에러를 냈다. `FUSION_DISABLE_ADDRESSABLES` 정의를 Android/Standalone/Server에 추가해 해결했다 (`Spellbound > 4`). Addressables를 쓰기로 하면 이 정의를 지운다.
- 에디터 콘솔의 **Error Pause**가 켜져 있으면 에러 로그 한 줄에 서버가 멈춘다. 서버 테스트 중에는 끄는 것을 권장.
- HUD 텍스트는 영문이다 (기본 폰트에 한글 글리프가 없을 수 있음). 정식 HUD는 ㅈㅇㅈ가 이벤트만 구독해 교체한다 (`UI/C12_HUD/BattleHudView.cs` 참고).
- 핸드 메시 표시는 셋업 도구가 넣은 `OVRHandPrefab`이 담당한다. 프리팹을 지우면 런타임에 스켈레톤만 자동 생성된다(손은 안 보임).
- 커밋 · 브랜치(`feature/cgo-*` 등, §30)는 팀 규칙대로 직접 진행한다.

---

## 9. 주요 파일 위치

| 영역 | 경로 (`UnityClient/Assets/_Project/`) |
| --- | --- |
| 진입점 · 모드 선택 | `Core/GameBootstrap.cs` |
| 서버 메인 루프 | `Combat/MatchSimulation*.cs` |
| 데미지 파이프라인 §12.5 · 타겟 규칙 §11 | `Network/Authority/ServerDamageResolver.cs`, `TargetRuleValidator.cs` |
| 로컬 호스트 / Fusion 서버 · 클라이언트 | `Network/Session/LocalMatchHost.cs`, `FusionServerLauncher.cs`, `ClientConnector.cs` |
| 상태 동기화 | `Network/Sync/NetworkMatchState.cs`, `NetworkPlayer.cs`, `MatchSnapshot.cs` |
| 게임 이벤트 발행 (§25.2) | `Network/Events/NetworkGameEventPublisher.cs` |
| 오른손 시전 상태 머신 §6.2 | `Input/RightHand/CastStateMachine.cs` |
| $P+ 인식기 | `Gesture/Recognizer/PDollarPlusRecognizer.cs` |
| 스킬 19종 | `Spells/Definition/SpellDefinition.cs`(기본값) · `Spells/Executor/*Executor.cs` · `Spells/Behaviors/` · `Spells/Effects/` |
| 월드 표시 (유닛 크기 배율 · 타워 파괴 연출) | `Presentation/ArenaStateView.cs` |
| 봇 | `Bot/BotPlayer.cs`, `Bot/Brains/TimerRandomBrain.cs`, `ManualKeyBrain.cs` |
| 에디터 도구 | `Editor/SpellboundSetupWizard.cs`, `SpellboundBuildMenu.cs`, `SpellboundDebugMenu.cs` |

---

## 10. 2026-10-24 스킬 개편 후 확인할 것 (에디터 Play로 직접)

코드는 Unity 6 DLL로 컴파일 확인만 했다. 아래는 화면으로 직접 봐야 한다.

| # | 확인 | 방법 |
| --- | --- | --- |
| 10-1 | 인식된 룬 아이콘이 손가락(키보드 모드: 마우스 커서)을 따라다니는지 | 룬 그리기 → `F` → 마우스 이동 |
| 10-2 | S02 독병이 조준 지점으로 날아가 3열 띠 장판이 그 깊이에 깔리는지 | 조준 중 직사각형 띠 미리보기 확인 |
| 10-3 | S05 회오리가 레인을 쓸며 적 유닛을 뒤로 밀어내는지 | 미니언이 많은 레인에 시전 |
| 10-4 | S06 번개 범위(레인 폭 × 세로 9m)가 조준 위치를 따라가는지 | 조준 미리보기 · 낙뢰 위치 |
| 10-5 | H03 흡혈: 상대가 다른 레인에 있을 때 그 레인 유닛 · 터렛에서 회복되는지 | 봇 수동 조종(`Alt`)으로 상대 위치 고정 |
| 10-6 | H05 분노 띠(폭 = 타워~넥서스 거리)를 원하는 깊이에 놓을 수 있는지 | |
| 10-7 | 오크 · 드래곤 · 터렛 · 소환탑 크기 | 어색하면 `ArenaStateView`의 `axeOrcScale` · `dragonScale` · `turretScale` · `summonHutScale` |
| 10-8 | 타워 · 넥서스가 HP 0에서 사라지고 돌무더기가 남는지 | `F1`~`F8` 테스트 시나리오의 구조물 파괴 |
| 10-9 | (선택) 독병 · 분노 띠 VFX를 새로 만들기 | `Spellbound > Skills > Build All Skill Assets` — 안 해도 동작한다 (독병은 초록 구체로 대체) |

---

## 11. 메타 UI (C01~C13, Unity) — 2026-10-25

- `GameBootstrap > Meta Ui`(기본 켜짐): Play하면 **C01 로그인**부터 뜬다. 끄면 예전처럼 바로 봇전(전투만 테스트).
- 서버(`Backend/run_server.bat`)를 켜 두면 웹과 같은 계정 · 덱 · 친구 · 우편 · 랭킹을 쓴다. 서버 없이 보려면 C01의 **"서버 없이 시작 (오프라인 봇 연습)"**.
- Quest에서는 C01 아래 **주소 변경**으로 서버 PC의 IP(예: `http://192.168.0.10:8080`)를 넣는다 (`Backend/open-firewall.bat`).
- 조작: 데스크톱 = 마우스, VR = 오른손 레이로 가리키고 핀치(컨트롤러는 트리거). 입력칸을 고르면 VR 화면 키보드(영문 · 숫자)가 뜬다. 전투에 들어가면 UI 레이는 꺼진다.
- 흐름: C02 로비 → 랭킹전(C10 매칭) / 친선전(C06 룸) / 봇 연습전 → C11 카운트다운(여기서 전투 시작) → C12 HUD → C13 결과 → 다시 대전 · 로비로 (씬을 다시 불러와 전투를 지운다).
- 랭킹전 · 친선전도 로컬(LocalVsBot) 모드에서는 상대가 봇이다 — 실제 사람 대 사람은 PC 서버(FusionClient 모드)로 접속해야 한다. 전적은 각 모드로 저장된다.
- 디자인: 팀 VR UI 프로토타입(Figma Make · React, 저장소 루트 `UI/` — 이식을 마치고 2026-10-25 삭제)을 따른다 — 보라 네온 · 도는 마법진 · 떠오르는 입자 · 모서리 ㄱ자 테두리, 영문 제목 Cinzel.
  프로토타입 화면 → Unity: Login · SignUp → C01, Start → C02 로비, Settings → C04, Result → C13, Tutorial → 로비의 **튜토리얼**(`UI/Tutorial`, 실제 손동작 5단계 + 연습 = 스킬 테스트).
  HUD · SkillHUD · Targeting → C12 전투 HUD(HP 막대 · 원형 쿨타임 슬롯 · 방어 계층 알약), RuneCasting(룬 그리기 연습)은 전투의 룬 궤적 · 인식 표시와 C09 마법서의 "그리는 룬"이 맡는다.
  프로토타입의 마나 · 킬 수 · 궁극기 충전 · Q/W/E/R 은 이 게임 규칙에 없어서 옮기지 않았고, CharacterSelect(캐릭터 고르기)는 이 게임에 캐릭터가 없어 C09 마법서(덱 8장)로 대신한다.
  프로토타입에 없는 화면(C03 · C05~C11)은 같은 부품(UiKit)으로 같은 색 · 글꼴을 쓴다.
- **Google 로그인 (C01 "Google 계정으로 계속하기")** = 기기 로그인(OAuth Device Flow, `Backend/GoogleDeviceAuth.cs`): 카드에 `google.com/device` · 코드 · 남은 시간이 뜨고, 휴대폰 · PC에서 코드를 입력해 승인하면 자동으로 로그인된다. 처음 쓰는 Google 계정은 서버가 계정을 만든다.
  - **할 일 (1회):** Google Cloud Console → 사용자 인증 정보 → OAuth 클라이언트 `538311580843-8ikod…`(유형 "TV 및 입력 제한 기기") → **JSON 다운로드** → `UnityClient/Assets/_Project/Backend/Resources/GoogleOAuth/device_client.json`으로 저장 (형식은 같은 폴더 `device_client.example.json`). Google이 이 유형도 토큰 교환에 `client_secret`을 요구한다 — 없으면 버튼을 누르는 즉시 안내가 뜬다. 이 파일은 `.gitignore` 처리되어 있다 (팀원에게는 따로 전달).
  - OAuth 동의 화면이 "테스트" 상태면 **테스트 사용자**에 등록한 Google 계정만 로그인된다.
  - 서버는 관리자 설정 `auth.google_device_client_id`(기본값 = 위 ID)로 발급된 토큰도 받는다 — 웹 UI용 `auth.google_client_id`가 없어도 동작.
- 글꼴: `UI/Resources/Fonts/NotoSansKR-*.otf` · `Cinzel.ttf` (둘 다 SIL OFL). 스킬 그림: `UI/Resources/SkillArt` (웹 `Backend/static/assets/skills`와 같은 파일 — 바꾸면 둘 다 바꾼다).

## 11-1. S02 독 장판 룬을 삼각형 + X로 교체 (2026-10-25)

- 원본: 루트의 `image (4).png` (삼각형 + 아래쪽 안의 X). 스킬은 그대로 독 장판이고 **그리는 룬만** 바뀌었다 (마법진 · 효과 · 쿨타임 변화 없음).
- 바뀐 곳: `Art/Runes/Designs/S02.png` · `Art/Runes/Icons/S02_s.png`(손 옆 아이콘) · `Backend/static/assets/skills/rune|rune_line/S02.png` · `UI/Resources/SkillArt/rune/S02.png` · `RuneDesigns.cs`(좌표) · `Gesture/Templates/Data/Rune_S02_PoisonField.asset`(샘플) · 웹 인식기 `wow_runes.py`.
- 인식기는 사람이 다르게 그리는 방식(삼각형을 2 · 3획으로, X를 한 붓으로, 전부 한 번에 이어서)도 알고 있다 (`RuneDesigns.ExtraStyles`). 오프라인 시험: 모든 방식 96~100% 시전, 다른 룬 정확도 그대로.
- 룬 모양을 또 바꿀 때: `RuneDesigns.cs` 좌표 + `Designs/*.png` + 웹 이미지를 고친 뒤 메뉴 `Spellbound > Runes > Build Rune Templates` 로 템플릿 에셋을 다시 만든다 (이번에는 같은 코드로 에셋 YAML을 직접 다시 써서 메뉴를 거치지 않았다).

## 12. 스킬 19종 전부 테스트하는 방법

1. `GameBootstrap > Recognize All Runes`가 켜져 있는지 확인한다 (기본 켜짐). 로컬 봇전에서는 **덱에 없는 룬도 그리면 바로 시전**된다 — 쿨타임 · 고위력 횟수 없음.
2. Play → C02 로비의 **"스킬 테스트 (19종 전부)" → 시작** (메타 UI를 끄면 Play 즉시 봇전).
3. Game 뷰를 한 번 클릭하고 마우스 왼쪽 드래그로 룬을 그린다 (모양: C09 마법서의 "그리는 룬" 또는 `Assets/Art/Runes/Designs/`). 여러 획이면 드래그를 나눠 그린다.
4. `F` = 주먹(그리기 끝 · 인식). 조준이 필요 없는 스킬(S08 · S09 · S10 · H01 · H04)은 여기서 바로 시전.
5. 조준 스킬은 `G` = 포인팅 → 마우스로 열(·깊이)을 가리키고 2초 유지하면 시전. `X` / 우클릭 = 취소, `A` / `D` = 열 이동.
6. 덱 안의 스킬은 숫자키 `1`~`8`로 룬 없이 바로 시전할 수도 있다 (슬롯 순서 = C09 덱 순서). 덱을 바꾸면 그 8장이 숫자키에 들어간다.
7. 인식 결과 · 시전 확정은 Console에 `[CastStateMachine]` · `[RuneTest]` · `[MatchSimulation] 시전 확정` 으로 찍힌다.
8. 소환 · 설치(S11 · S12 · S13 · H06) 확인이나 상황 만들기: `F1`~`F8` 테스트 시나리오(타워 파괴 등), `Alt`+키 봇 수동 조종 (`Docs/Bot/ManualControlKeys.md`).
9. 헤드셋: 오른손 핀치로 그리기 → 주먹 → 검지 포인팅으로 조준 (§2). PC 서버 접속 모드에서는 덱 안의 룬만 시전된다.
