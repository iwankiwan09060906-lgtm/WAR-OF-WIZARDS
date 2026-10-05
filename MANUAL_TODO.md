# Spellbound VR — 사람이 직접 해야 하는 일

> 대상: 4인 팀 (ㅊㄱㅇ · ㅈㅈㅇ · ㅈㅇㅈ · ㅇㅎㅅ)
> 기준: `Spellbound_VR_Master_Prompt_v6.0.md`, 작업일 2026-10-03

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

구현 범위: 핵심 데이터 모델 · 서버 권한 시뮬레이션(메인 루프) · 입력/제스처/조준 · 스킬 **S03 파이어볼 / S07 총난사 / S08 방어 쉴드** · 기본 봇 · 간이 HUD · Fusion 서버/클라이언트.
나머지 16종 스킬, 13개 메타 UI, Supabase, 매칭 큐 UI, FSM 봇은 요청대로 제외했다.

---

## 1. 지금 바로 해볼 것 (헤드셋 없이, 5분)

1. Unity에서 `Assets/Scenes/Client/04_Battle.unity`를 열고 **Play**를 누른다.
2. Game 뷰를 한 번 클릭해 포커스를 준다 (포커스가 없으면 Input System이 키보드를 무시한다).
3. 조작 (자세한 표: `Docs/Bot/ManualControlKeys.md`)
   - `1` 파이어볼 · `2` 총난사 · `3` 방어 쉴드 (마우스 위치로 2초 조준 후 시전)
   - 마우스 왼쪽 드래그로 룬 그리기 → 원(○)=파이어볼, Z=총난사, 역삼각형(▽)=쉴드 → `F`(주먹) → `G`(포인팅)
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

- 현재 템플릿은 **절차 생성 도형**(원 · Z · 역삼각형)이다: `Assets/_Project/Gesture/Templates/Data/Rune_S0x_*.asset`.
- 인식할 때마다 콘솔에 `[CastStateMachine] $P+ → S03 d=2.41 2nd=S08 d2=5.80 → None` 형식으로 거리가 찍힌다.
- 판정 임계값: `Gesture/Runtime/GestureValidator.cs`의 `GestureValidationSettings` 초기값 — `MaxAcceptDistance 7.0`, `MinSecondBestRatio 1.08` (합성 노이즈 300회 실험값: 정확도 원 100% · Z 100% · ▽ 99%).
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
| `_Project/Core/Data/MatchRuleConfig.asset` | 플레이어 HP 1000, 타워 1500 / 넥서스 2000, 타워 공격 35, 넥서스 회복 25, 강화당 공격 +15% · 쿨타임 -10%(하한 50%), 봇 매칭 대기 10초, 재시작 10초 |
| `_Project/Spells/Definition/Data/Spell_S03_Fireball.asset` | 피해 120, 반경 2.6m |
| `Spell_S07_Gatling.asset` | 8발 × 22, 0.12초 간격, 탄속 12m/s (상대 발판까지 약 1.7초 → 회피 가능 비행 시간, §11.2) |
| `Spell_S08_Shield.asset` | 흡수 180, 8초, 3초 무피격 후 초당 12 회복 |
| `_Project/Combat/Minion/Data/Minion_*.asset` | 근접 120HP/14, 원거리 80HP/11, Brute 420HP/30 · 구조물 ×2 |
| `_Project/Bot/Data/BotConfig.asset` | 시전 간격 4~8초 |

- `MatchRuleConfig.AllowPartialDeck = true` — 19종이 완성되기 전까지 **8장 미만 덱(빈 슬롯)**을 허용한다. 19종 완성 후 false로.
- 내 덱은 임시로 `GameBootstrap.localDeck`(S03, S07, S08)에서 온다. 마법서(C09, ㅈㅇㅈ) 연동 시 여기로 넣으면 된다.

---

## 7. 남은 스킬 16종 추가 방법 (ㅊㄱㅇ, Rule 11)

1. `Spell_Sxx_Name.asset` 생성 (`Create > Spellbound > Spell Definition`) → `SpellCatalog.asset`에 추가.
2. `Executor` · `Behavior` 지정. 이미 있는 조합이면 코드 없이 끝난다.
3. 새 동작이면 `Spells/Behaviors/`에 Behavior 추가 → 해당 Executor의 `switch`에 연결.
   - Beam / Summon / Structure / Global Executor는 아직 없다 → `Spells/Executor/`에 `ISpellExecutor` 구현 추가 후 `SpellExecutorRegistry.Create`에 등록. (미등록 스킬은 서버가 `NotImplemented`로 거절, 쿨타임 미소모)
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
- 이 폴더는 현재 Git 저장소가 아니다. 커밋 · 브랜치(`feature/cgo-*` 등, §30)는 팀 규칙대로 직접 진행한다.

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
| 스킬 3종 | `Spells/Executor/{Area,Projectile,Buff}Executor.cs`, `Spells/Behaviors/`, `Spells/Effects/ShieldRegenEffect.cs` |
| 봇 | `Bot/BotPlayer.cs`, `Bot/Brains/TimerRandomBrain.cs`, `ManualKeyBrain.cs` |
| 에디터 도구 | `Editor/SpellboundSetupWizard.cs`, `SpellboundBuildMenu.cs`, `SpellboundDebugMenu.cs` |
