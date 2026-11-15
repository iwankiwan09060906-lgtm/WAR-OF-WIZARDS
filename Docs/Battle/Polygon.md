# 다인전 (정N각형 개인전) — 규칙 · 구현 상태

1:1 · 4인 개인전과 별개인 세 번째 모드. 도형의 **점(플레이어)** 과 **선(레인)** 이 모티브다.

```text
   N = 5 (정오각형)                 탈락하면 N-1각형으로 다시 짠다
          0                          N = 2 가 되면 1:1 전장 (마주 보고 레인 3개)
      1 ·───· 4      ─── 변 = 이웃 레인 (소환 · 설치 · 웨이브)
       \  ╳  /       ··· 대각선 = 직접 공격만
        2───3
```

## 규칙

| 항목 | 내용 | 코드 |
| --- | --- | --- |
| 인원 | 3 ~ 6명 (사람 + AI). 들어온 사람이 3명이 되면 카운트다운, 카운트다운 중에 들어온 사람도 낀다. 시작 순간 인원이 N | `MatchRuleConfig.PolygonMinPlayers` · `AllSeatsReady` · `BeginPlaying` |
| 자리 | 자리 번호(1 ~ N) 순으로 정N각형 꼭짓점 (1번이 아래, 시계 방향 — 1번의 왼쪽 이웃이 2번). 이웃 사이 거리 = 변 길이 | `ArenaGeometry.SetRing` |
| 레인 | 두 명마다 레인 하나 (번호 = 쌍, 15개). 이웃끼리 = 변, 나머지 = 대각선. 레인은 발판 가운데 칸끼리 잇는다 | `PairLane` · `IsSpawnLane` |
| 상대 고르기 | 열(Column) 값으로: Left = 왼쪽 이웃, Right = 오른쪽 이웃, Center · 3 · 4 = 시계 방향 2 · 3 · 4번째. 지금 인원에 없는 열은 `InvalidTarget` | `ColumnTarget` · `ColumnValid` |
| 직접 공격 | 누구에게나 (변 · 대각선). **시전 순간 상대가 선 칸**을 노리므로, 날아오는 동안 칸을 옮기면 피한다 | `MatchSimulation.GetAimColumn` |
| 소환 · 설치 | 좌 · 우 변에만. 대각선에 쓰면 `InvalidTarget` (쿨타임 소모 없음) | `ValidateLane` |
| 웨이브 | 좌 · 우 변으로만 나간다 | `WaveSpawner` |
| 구조물 | **넥서스만** (타워 없음). 넥서스가 서 있는 동안 상대 유닛은 나를 못 치고 HP 가 회복된다. 스킬 피해 감소 없음 | `ResetStructures` · `DefenseLayerState` |
| 체력 | 플레이어 HP **3배** (타워가 없어 스킬이 처음부터 100% 들어오므로) | `MatchRuleConfig.PolygonPlayerHpScale` · `ApplyPolygonRules` |
| 탈락 | 4인 개인전과 같다 (순위 = 탈락 순서의 반대, 기권 · AFK · 봇 인계 · 관전 · 마나 폭풍) | `TickFfaResult` |
| 재구성 | 탈락하면 남은 사람으로 링을 다시 잡는다: 유닛은 주인 시점 **같은 쪽(좌 · 우) 새 레인**으로 — **설치물은 HP 그대로 레인 위 같은 깊이에 다시 서고, 미니언 · 소환수는 HP 그대로 출발 지점부터 다시** 나간다. 날아가던 투사체 · 진행 중 스킬은 지우고, 넥서스는 HP 그대로 새 자리로 | `RememberLanes` · `RebuildPolygon` |
| 2명 남음 | **1:1 전장으로 전환**: 마주 보고 평행 레인 3개 (번호 15 ~ 17), 열 = 1:1 처럼 상대 발판의 칸. 웨이브 · 소환 모두 3레인 | `PolygonDuelStage` · `DuelLike` |

클라이언트는 스냅샷의 `PlayerSnap.Present` · `Place` 로 링을 알 수 있다 (경기 중 `Present && Place == 0` 인 자리). 네트워크 필드는 늘지 않았다.

## 바뀐 공용 값 (다른 모드에도 영향)

- `Team` 에 `Seat5` · `Seat6`, `TeamUtil.TeamCount` 4 → 6, `MatchSnapshot.PlayerCount` 6 (구조물 24칸), `MatchWinner.Seat5` · `Seat6`.
- 4인 개인전 왼쪽 · 오른쪽 타워 ID 가 18 ~ 25 → 22 ~ 33 으로 밀렸다 (`TeamUtil.TowerId`).
- **스냅샷 크기가 바뀌었으므로 서버 · Quest 를 함께 다시 빌드해야 한다.**

## 로비 · 매칭 · 서버

- 로비 인원 버튼 **다인전** (`MatchContext.Players = 6` = 다인전 표시, `ArenaMode.Polygon`). 랭킹전 · 친선전 · 봇 연습전이 모두 따른다.
- 백엔드: `{format: "poly"}` 또는 `{players: 6}` → 다인전 큐 (`max_players = 6` 이 모드 표시, 실제 자리 수는 `match_session_players`). 사람을 6명까지 모으고, `matchmaking.bot_timeout` 이 지나면 모인 사람으로 시작하되 3명이 안 되면 봇으로 3명까지 (`seat_target`). 친선전 방은 6자리, 시작할 때 3명 미만이면 봇.
- 게임 서버: `-polygon` (세션 `이름-poly`, 로그 `server_poly.log`). `StartServer.bat` 이 4인 · 1:1 과 같이 켠다. 서버 빌드 메뉴가 `RunServerPolygon.bat` 도 만든다.
- 게임 서버의 30초 AI 채우기는 **3명까지만** (`NetworkMatchState.TickBotFill`). 3명이 되면 카운트다운, 그 사이 들어온 사람도 낀다. 경기가 시작된 뒤 들어온 사람은 끼지 않고 관전한다.
- 봇 연습전(오프라인)은 빈자리를 모두 AI 로 채워 정육각형.

## 표시

- `FfaArenaView` 가 다인전도 그린다: 변 = 돌다리(벽 · 등불 · 룬 문양), 대각선 = 가운데 심연을 가로지르는 옅은 빛줄기 (**평소엔 숨기고 그 상대를 조준하는 동안만** 보인다), 가운데 아래 룬 핵, 변 바깥 성채 섬, 떠다니는 바위, 자리마다 1번 발판 복제 · 번호판.
- 링이 바뀌면 전장을 통째로 다시 짓는다. 클라이언트는 스냅샷의 `MatchSnapshot.RingMask()` 로 링을 맞춘다 (`FfaArenaView` 가 가장 먼저 돈다).
- 내 리그는 새 자리로 순간 이동 (`CommanderPlatform`, 비네팅 연출). 넥서스 · 잔해는 서버 자리(U · V)를 따라가고, 탈락한 자리의 아바타 · 넥서스는 사라진다 (`ArenaStateView`).
- 조준: 가리킨 방향에 가장 가까운 상대 (대각선 너머 포함). 소환 · 설치는 좌 · 우 이웃 레인 중 가까운 쪽으로 붙는다 (`ResolveAim(spawnOnly)`).
- HUD: `PolygonHudPanel` — 상대 카드 최대 5장 (왼쪽 이웃 → 오른쪽 이웃 순, 이웃 / 건너편 표시), 정N각형 지도 (변 진하게 · 대각선 옅게 · 넥서스 · 유닛). 방어 칸은 "타워 없음 · 넥서스". 대기 화면 6자리.
- 관전은 링을 시계 방향으로 돈다. 얼음(H01) 바닥 · 연출 방향도 지금 링 기준.

## AI

- 봇 두뇌는 열 5개(0 ~ 4)를 본다. 지금 인원에 없는 열은 닫힌 레인, 대각선은 소환 금지 (`LaneInfo.NoSpawn`).
- 피하기 실수: 날아오는 투사체 하나마다 `BotConfig.DodgeMissChance`(기본 40%) 확률로 못 피한다 (모든 모드 봇 공통).
- 노릴 상대(`FocusEnemy`): HP 가 낮은 쪽, 웨이브 · 소환이 닿는 이웃 우선, 넥서스가 무너진 이웃은 더 먼저. 타워가 없어 스킬은 늘 상대 본체를 노릴 수 있다.
- VLM · AI 코치: 서버 지도는 3열이라 다인전은 왼쪽 이웃 · 첫 건너편 · 오른쪽 이웃만 보낸다. 작전이 쓸 수 없는 열을 찍으면 봇이 거른다.
- BotLab 측정 (`poly 40`, HP 3배 · 피하기 실수 포함): 3명 평균 274초 · 4명 289초 · 5명 291초 · 6명 309초 (3분 30초부터 마나 폭풍), 40판 모두 끝남, 봇 시전 4179번 중 레인 거절 2번.

## 남은 일 · 조정 거리

- 다인전 전장 아트는 실행 중 생성이라 헤드셋에서 크기 · 시야(변 길이 `ArenaLayout.polygonEdgeLength` 32m)를 직접 보고 맞춰야 한다.
- 재구성 순간 연출은 리그 순간 이동 비네팅뿐 — 필요하면 전장 페이드를 더한다.

## 검사

```text
dotnet run -c Release -- polytest     # Tools/BotLab — 정N각형 배치 · 조준 · 변/대각선 · 시작 인원 · 재구성 · 1:1 전환 · 클라이언트 링 동기화 · 6명 봇전
dotnet run -c Release -- poly 40      # 봇전 측정: 인원별 경기 길이 · 자리별 1위 · 봇 시전 거절률
python Backend/test_web_ui_api.py     # 다인전 큐 · 봇 채우기 · 친선전 방 포함
```
