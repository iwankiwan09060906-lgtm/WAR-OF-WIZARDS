# 4인 개인전 (FFA) — 전장 · 규칙 · AI · VFX

1:1 은 그대로 두고 4인 개인전을 더했다. 로비의 **4인 개인전 / 1:1 대전** 버튼으로 고르며, 랭킹전 · 친선전 · 봇 연습전이 모두 이 인원을 따른다.

```text
        1 (Home)
      / | \          발판 4개가 마름모, 모든 쌍이 레인 1개로 이어진다 (완전 그래프, 레인 6개)
 4 ───┼───── 2       내 왼쪽 · 가운데 · 오른쪽 레인 = 왼쪽 이웃 · 맞은편 · 오른쪽 이웃
      \ | /          대각선 지름 = fieldLength 41m, 옆 레인 약 29m
        3 (Away)
```

## 전장

- **조준 = 상대 고르기.** 세 열이 곧 세 상대다. 룬 · 조준 입력은 1:1 과 같고, 가리킨 방향에 가장 가까운 레인이 고른 상대가 된다 (`ArenaGeometry.ResolveAim`).
- **칸 이동 = 회피.** 투사체는 쏜 순간 상대가 선 칸으로 날아가고, 도착 전에 칸을 옮기면 빗나간다.
- **타워 = 레인마다 하나.** 각자 세 상대로 가는 레인 위에 타워를 하나씩 세운다 (진영당 3개, 넥서스는 1개). 스킬 피해 감소 · 공격 보너스 · 넥서스 잠금이 레인별로 따로 움직인다.
- 씬 아트는 1:1 직사각형이라 개인전이면 `FfaArenaView` 가 실행 중에 바꾼다: 1:1 벽 · 바닥 · 절벽을 끄고, 바닥 · 레인 6개(이중선) · 번호판을 만들고, 2 · 4번 발판은 1번 것을 복제한다. 구조물은 `ArenaStateView` 가 1번 것을 복제해 세운다 (옆 레인 타워 포함).
- 구조물 깊이는 `ArenaLayout.ffaTowerDepth` 9m(레인을 따라) · `ffaNexusDepth` 4m (옆 레인에서도 닿도록 1:1 보다 발판 가까이). 이 오브젝트들은 옮겨야 하므로 정적 배칭을 껐다.
- 구조물이 1:1 보다 가까워 커 보이므로 시야를 `ffaViewLift` 3.5m 올리고(눈높이 약 13.5m) 타워 · 넥서스 · 상대 아바타를 `ffaModelScale` 0.65 배로 줄인다. 표시만 바뀌고 판정은 그대로다.

코드: `ArenaGeometry` (`ArenaMode` · `LaneFrame`) — 레인 위 계산은 전부 `LaneFrame` 으로 하므로 1:1 과 개인전이 같은 스킬 코드를 쓴다.

## 규칙 (1:1 과 다른 점)

| 항목 | 값 | 어디서 |
| --- | --- | --- |
| 진행 | 한 명씩 탈락, 마지막 1명 승리. 순위 = 탈락 순서의 반대 (같은 틱이면 같은 순위) | `MatchSimulation.TickFfaResult` |
| 탈락 | 구조물 · 유닛이 사라지고 그 레인이 닫힌다 (시전 거절 `LaneClosed`). 그쪽으로 가던 유닛은 다음 상대의 레인으로 갈아탄다 | `Eliminate` · `ReassignLanes` |
| 구조물 HP | 1:1 의 50% (타워 하나당 1500 · 넥서스 2000) | `MatchRuleConfig.FfaStructureHpScale` |
| 받는 스킬 피해 | 공격자 쪽 레인의 타워가 있으면 40% (1:1 20%), 그 타워가 없으면 그 공격자만 100% | `FfaTowerAliveSkillDamageRatio` · `DefenseLayerState.SkillDamageRatio` |
| 강화 간격 | 120초 (1:1 300초) | `FfaScalingInterval` |
| 공격력 보너스 | 타워 +20% = 상대의 내 쪽 레인 타워를 무너뜨렸을 때만, 넥서스 +40% = 넥서스를 잃은 그 상대를 칠 때만 | `TeamAttackMultiplier` |
| 넥서스 잠금 | 내 쪽 레인 타워를 무너뜨린 상대의 유닛만 넥서스를 칠 수 있다 | `TargetRuleValidator.IsNexusLocked` |
| S09 타워 버프 · H01 프리즈 | 서 있는 타워 전부에 걸린다. S09 슬롯은 타워를 모두 잃으면 꺼진다 | `BuffTower` · `FreezeTower` |
| 마나 폭풍 | 3분 30초부터 5초마다 서 있는 타워 전부(모두 무너지면 넥서스) -60, 플레이어 -6 (칠 때마다 +1) | `Storm*`, `TickStorm` |
| 사람이 나감 | 그 자리를 봇이 이어서 싸운다 (HP · 슬롯 · 구조물 유지) | `TakeOverWithBot` |
| 기권 | 지금 순위로 탈락, 관전 없이 결과 화면 | `Forfeit` |
| AFK | 지금 순위로 탈락 | `TickFfaResult` |
| 탈락 | 남은 사람을 관전한다 (아래 **관전**) | `SpectatorView` |
| 싸우는 사람도 관전하는 사람도 없음 | 봇끼리 끝까지 두지 않고 남은 HP 순으로 순위를 매겨 끝낸다 | `AnyHumanWatching` · `EndFfaWith` |

마나 폭풍은 BotLab 측정에서 나온 결정이다. 웨이브끼리 레인 중간에서 상쇄돼 구조물 HP · 강화만 조정해서는 200판 모두 15분 안에 끝나지 않았다.

## 관전

탈락하면 순위 · 전적은 그 자리에서 저장되고, 경기가 끝날 때까지 남은 플레이어(AI 포함)의 시점을 돌아가며 본다. 1:1 과 기권은 관전 없이 바로 결과 화면이다.

| 조작 | PC | 헤드셋 |
| --- | --- | --- |
| 다음 · 이전 플레이어 | Tab · → / ← | 컨트롤러 A / B, 또는 관전 메뉴 버튼 (핸드트래킹) |
| 관전 메뉴 (다음 · 이전 · 계속 관전 · 결과 보기) | Esc | 왼손 메뉴 제스처 · 컨트롤러 ☰ |

- 리그를 보는 사람의 발판 칸으로 옮기고 그 사람 아바타를 끈다. 칸을 옮기면 따라가고, 탈락하면 다음 사람으로 넘어간다.
- HUD 의 HP · 방어 · 위치 · 세 상대 카드 · 지도, 아군 / 적군 색은 보는 사람 기준으로 바뀐다. 8슬롯은 내 덱 그대로다.
- 경기가 끝나면 2.5초 뒤, "결과 보기"를 고르면 바로 결과 화면으로 간다. 관전자가 모두 나가면 서버가 경기를 끝낸다.

## 30초 AI 채우기

- 게임 서버(`NetworkMatchState.TickBotFill`): 첫 사람이 들어오고 30초(`BotFillWaitSeconds`) 안에 안 들어온 자리를 AI 가 대신한다. 대기 중에 사람이 오면 AI 가 자리를 내준다.
- 백엔드: 대기열은 `matchmaking.bot_timeout`(기본 30초, 15초에서 변경), 매칭 뒤 안 들어온 사람은 `match.join_timeout`(기본 30초)이 지나면 AI 가 대신하고 경기가 시작된다.
- 이미 만들어진 서버 DB 는 설정 값이 그대로 남으므로 관리자 콘솔에서 `matchmaking.bot_timeout` 을 30 으로 바꿔야 한다.
- 대기 화면(C11)에 자리 네 칸 · "입장 2 / 4명 · 18초 뒤 빈자리는 AI" 가 나온다.

## AI 적용

| 기능 | 적용 |
| --- | --- |
| **봇 두뇌** | 레인마다 상대를 따로 본다. 노릴 상대 = 타워가 무너진 쪽 → HP 낮은 쪽, 동점이면 왼쪽 이웃. 소환을 그 상대의 레인에 몰아준다. 개인전 가중치 `AiBrainWeights.Ffa` |
| **VLM 작전** | 서버가 레인마다 상대 진영을 그린 지도(`wow_board._ffa_tops`)로 VLM 에 묻는다. 작전에 `focusColumn`(노릴 상대)이 추가됐고, 탈락한 상대 · 닫힌 레인은 서버가 고친다 |
| **AI 코치** | 사람에게도 전투 중 조언을 보여 준다 (`Backend/BattleCoach.cs`, HUD 오른쪽 아래). 로그인 + 서버가 있을 때만, 판정에는 영향 없음 |
| **MediaPipe** | 헤드셋 없는 PC 에서 웹캠으로 플레이. 환경설정 > 제스처 > "웹캠 손 입력" + `GestureTools/MediaPipe/run_hand_tracker.bat` (`Input/MediaPipe/MediaPipeHandInput.cs`) |
| **학습** | 봇 판 결과를 순위(`place` · `players`)와 함께 보낸다 |

## VFX (`vfx.unitypackage` → `Assets/VFX`)

팩은 그림 순서(S01~S20)로 번호가 붙어 게임 스킬 번호와 다르다. 다시 임포트해도 어긋나지 않게 파일 이름은 그대로 두고 `VFXCatalog.PackAliases` 대응표로 잇는다.

| 게임 스킬 | 팩 프리팹 | 게임 스킬 | 팩 프리팹 |
| --- | --- | --- | --- |
| S01 연막 | `S01_LOS Blocker` | S10 집중 | `S16_player Buff` |
| S02 독 장판 | `S02_poison` | S11 도끼 전사 | `S17_Melee Minion` |
| S03 파이어볼 | `S04_fireball` | S12 드래곤 | `S18_Caster Minion` |
| S04 화살 세례 | `S05_arrow` | S13 터렛 | `S19_turret tower` |
| S05 토네이도 | `S06_tornado` | H01 프리즈 | `S03_timestop` |
| S06 번개 | `S07_thunde` | H02 레이저 | `S10_laser` (시전만) |
| S07 총난사 | `S09_shootingr` | H03 흡혈 | `S08_Lifesteal` |
| S08 쉴드 · H04 반사 | `S11_Shield` (시전만) | H05 분노 · H06 소환탑 | `S14_Minon Buff` · `S20_turret Minion` |
| S09 타워 버프 | `S15_tower Buff` | 피격 공통 | `S00_hit` |

쓰지 않은 팩 프리팹: `S10_laser_Projectile`(빔은 길이 1m 규격이 달라 기존 유지) · `S11_Shield_8s_Projectile` · `S14_Minon Buff_Projectile 1` · `S15_tower Buff_Projectile` · `S07_thunde_Projectile`. 앞의 넷은 크기 규격을 눈으로 맞춰야 하고, 마지막 것은 찾을 수 없는 스크립트를 참조한다.

## 서버 실행

저장소 맨 위의 `StartServer.bat` 하나면 된다: 백엔드 + 로컬 VLM(있으면) + 4인 · 1:1 게임 서버를 창 없이 켜고, 게임 서버가 꺼지면 다시 켠다. 창을 닫으면 게임 서버도 같이 꺼진다. PC 에서 게임을 켤 때 서버가 꺼져 있으면 게임이 이 파일을 실행하고, `ServerAutostart.bat` 을 한 번 실행하면 Windows 로그인 때 저절로 켜진다 (다시 실행하면 해제).

서버 하나가 한 종류만 연다: `RunServer.bat` = 4인 개인전 (세션 `이름-ffa`), `RunServer1v1.bat` = 1:1. 서버 창을 보고 싶을 때 쓴다. 서버 빌드 메뉴로 다시 빌드하면 두 파일이 함께 만들어지고, 빌드하는 동안 돌던 게임 서버는 꺼졌다가 끝나면 다시 켜진다.

서버와 클라이언트의 구조물 동기화 크기(`MatchSnapshot.StructureCount`)가 같아야 하므로 이 값이 바뀌면 서버 · Quest 를 함께 다시 빌드한다.

## 검사

```text
dotnet run -c Release -- ffatest          # 레인 · 조준 · 탈락 · 순위 · 봇 인계 규칙 (Tools/BotLab)
dotnet run -c Release -- ffa 400 900      # 자리 공정성 · 경기 길이
dotnet run -c Release -- ffatrace 7 900   # 한 판을 1분마다
Unity.exe -batchmode -projectPath UnityClient -executeMethod SpellboundVR.EditorTools.SpellboundSmokeTest.Run -smokePlayers 4 -smokeOut 폴더
python Backend/test_web_ui_api.py
```
