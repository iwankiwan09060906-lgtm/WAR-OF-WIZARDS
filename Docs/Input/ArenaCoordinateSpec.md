# 전장 맵 — 좌표 기준과 구현

맵과 관련된 좌표의 기준 문서다. 전장은 **서버 판정용 수학 좌표**와 **눈에 보이는 아트**가 나뉘어 있고, 판정은 씬 오브젝트가 아니라 `ArenaGeometry` 만 본다.

| 경기 | 전장 | 표시 |
| --- | --- | --- |
| 1:1 | 직사각형, 평행한 레인 3개 | 씬 `04_Battle` 의 아트 (메뉴로 만들어 저장) |
| 4인 개인전 | 마름모, 레인 6개 | 씬 아트를 끄고 **실행 중에 코드로 생성** |

코드 위치 (`UnityClient/Assets/_Project/`)

| 파일 | 역할 |
| --- | --- |
| `Arena/ArenaGeometry.cs` | 좌표 계산 전부. MonoBehaviour 가 아니어서 서버 · BotLab 에서도 같은 코드가 돈다 |
| `Arena/ArenaLayout.cs` | 씬에 놓는 컴포넌트. 치수를 인스펙터에서 받아 `ArenaGeometry` 를 만든다 |
| `Arena/CommanderPlatform.cs` | 플레이어 리그(카메라)를 발판 칸으로 옮긴다 |
| `Arena/TeamArt.cs` | 보는 사람 기준 아군 · 적 재질 |
| `Presentation/FfaArenaView.cs` | 4인 전장 표시 생성 |
| `Presentation/ArenaStateView.cs` | 구조물 · 유닛 · 아바타 표시 |
| `Editor/SpellboundArenaBuilder.cs` · `SpellboundArenaArtBuilder.cs` | 메뉴 5 · 6, 1:1 전장 만들기 |

---

## 1. 좌표계

아레나 평면 (u, v), 단위 m.

- **u** = 1번 플레이어(Home) 기준 오른쪽 +
- **v** = 1번 발판 → 3번 발판 방향 +, 1번 발판이 v = 0
- **높이**는 판정에 없다. 표시할 때만 `ToWorld(u, v, height)` 로 더한다
- **깊이(Depth 0~1)** = 시전자 자기 진영 → 상대 진영 방향의 정규화 거리
- 레인 위 계산은 `LaneFrame` 으로 한다: 시전자 발판에서 상대 쪽으로 along m, 시전자 오른쪽으로 lateral m. 1:1 과 4인이 같은 스킬 코드를 쓰는 이유다

**열 번호는 두 가지다.** 요청과 이벤트의 열은 그 플레이어 자신의 시점(`Column` Left · Center · Right)이고, 서버 내부는 월드 열(0~2)이다. 3번 자리는 좌우가 반대다.

---

## 2. 1:1 전장

```text
 v=41  [Away 발판  R | C | L]        u = -5, 0, +5  (Away 는 마주 보므로 자기 왼쪽이 u = +5)
 v=38      Away 넥서스
 v=28      Away 타워
           ─── 레인 3개, 폭 5m ───    u = -5, 0, +5
 v=13      Home 타워
 v=3       Home 넥서스
 v=0   [Home 발판  L | C | R]        u = -5, 0, +5
```

| 값 | 씬 값 | 필드 (`ArenaLayout`) |
| --- | --- | --- |
| 전장 길이 | 41m | `fieldLength` |
| 레인 · 칸 간격 | 5m | `columnSpacing` |
| 넥서스 깊이 | 3m | `nexusDepth` (씬에 `homeNexusVisual` 이 연결되어 있으면 그 위치를 읽는다) |
| 타워 깊이 | 13m | `towerDepth` (〃 `homeTowerVisual`) |
| 미니언 스폰 깊이 | 6m | `spawnDepth` |
| 발판 윗면 | 0.52m | `commanderPlatformHeight` (표시 전용) |
| 눈높이 | 10m | 씬 `OVRCameraRig` 의 y |

아트는 메뉴 `Spellbound > 6. Apply Arena Art` 가 만든다. 바닥 · 낮은 벽 · 절벽 · 협곡 · 하늘 · 구조물 LOD 의 내용은 [`AssetIntegrationGuide.md`](../Art/AssetIntegrationGuide.md) 3.3 에 있다. 빈 부모 오브젝트(`Home_Tower` 등)의 위치가 곧 게임 로직 위치이므로 옮기지 않는다.

---

## 3. 4인 개인전 전장

발판 4개가 마름모로 놓이고 **모든 쌍이 레인 하나로 이어진다** (완전 그래프, 레인 6개).

```text
              1 (Home)  (0, 0)
            / | \
  (20.5,   /  |  \   (-20.5, 20.5)
   20.5)  4 ──┼── 2                  위에서 본 그림: 1번이 아래를 보므로 1번의 왼쪽이 그림의 오른쪽(2번)
           \  |  /
            \ | /
              3 (Away)  (0, 41)
```

| 값 | 내용 |
| --- | --- |
| 대각선 (1-3 · 2-4) | 41m (`fieldLength`) |
| 옆 레인 (1-2 · 2-3 · 3-4 · 4-1) | 약 29m |
| 레인 번호 | 0: 1-2 · 1: 2-3 · 2: 3-4 · 3: 4-1 · 4: 1-3 · 5: 2-4 |
| 자리 순서 | 시계 방향 1 → 2 → 3 → 4 (`TeamUtil.ClockIndex`). 진영 번호는 Home 0 · Away 1 · Seat2 2 · Seat4 3 이라 순서가 다르다 |
| 내 열 → 상대 | 왼쪽 = 시계 방향 다음 자리, 가운데 = 맞은편, 오른쪽 = 그다음 (`ColumnTarget`) |
| 칸 이동 | 발판 위 3칸을 옮기는 것뿐이고 레인은 바뀌지 않는다. 투사체는 쏜 순간의 칸으로 날아가므로 회피가 된다 (`ColumnAxis`) |
| 조준 | 가리킨 지점 방향에 가장 가까운 레인이 곧 상대다 (`ResolveAim`) |

### 구조물 배치

| 구조물 | 위치 | 개수 |
| --- | --- | --- |
| 넥서스 | 발판 앞 4m (`ffaNexusDepth`, 옆 레인에서도 닿는 거리) | 진영당 1 |
| 타워 | 세 레인 위 각각 발판에서 9m (`ffaTowerDepth`, 가운데 타워는 발판 정면) | 진영당 3 |

타워가 라인별로 있는 이유는 받는 피해 · 공격 보너스 · 넥서스 잠금이 **상대별로** 따로 움직이기 때문이다 (규칙: [`Ffa4.md`](../Battle/Ffa4.md)).

### 시야와 크기 (표시 전용)

구조물이 1:1 보다 가까워서(타워 13m → 9m) 그대로 두면 커 보이고 시야를 가린다. 그래서 4인전만 다음을 적용한다.

| 값 | 기본 | 필드 |
| --- | --- | --- |
| 시야 높이 | 1:1 보다 3.5m 위 (눈높이 약 13.5m) | `ffaViewLift` |
| 타워 · 넥서스 · 상대 아바타 크기 | 0.65배 | `ffaModelScale` |

판정 거리는 바뀌지 않는다. 두 값은 씬의 `[ArenaLayout]` 인스펙터에서 바로 조정한다. 데스크톱은 여기에 `desktopViewPitch` 28° 로 시점을 아래로 기울인다.

### 실행 중에 만드는 것 (`FfaArenaView`)

1:1 씬을 그대로 쓰고 경기가 4인이면 이렇게 바꾼다. 씬 파일은 바뀌지 않는다.

1. 1:1 전용 아트를 끈다: `Art_Walls` · `Art_Ground` · `Art_FarCliffs` · `Art_Decor` · `Art_Bridge` · `Lanes` · `Ground`
2. 바닥 원판(반지름 33m) · 가장자리 고리 · 가운데 표시를 만든다
3. 레인 6개를 만든다: 폭 5m 띠 + 양쪽 발광 테두리
4. 1번 발판 세 칸을 복제해 2 · 4번 자리에 세운다
5. 발판 뒤 높이 8m 에 자리 번호판을 띄운다
6. 구조물은 `ArenaStateView` 가 1번 것을 복제해 세운다 (옆 레인 타워 포함). 이때 크기는 곱하지 않고 값을 정한다 — 이미 줄인 1번을 복제하기 때문이다
7. 마나 폭풍이 시작되면 가장자리 고리가 보라색으로 빛난다

이 오브젝트들을 옮겨야 하므로 구조물 · 발판의 정적 배칭은 꺼 두었다.

---

## 4. 맵을 바꿀 때

| 바꾸려는 것 | 할 일 |
| --- | --- |
| 1:1 구조물 위치 | 씬 `Home_Tower` · `Home_Nexus` 등을 옮기거나 `ArenaLayout` 값 수정 후 메뉴 6 |
| 4인 구조물 위치 | `ffaTowerDepth` · `ffaNexusDepth`. 판정이 바뀌므로 `ffa 200 900` 으로 경기 길이를 다시 잰다 |
| 전장 크기 | `fieldLength` · `columnSpacing`. 웨이브 · 스킬 범위에 영향이 있어 BotLab 으로 다시 잰다 |
| 시야 · 크기 | `ffaViewLift` · `ffaModelScale`. 표시만 바뀌어 재측정이 필요 없다 |
| 4인 바닥 · 레인 모양 | `FfaArenaView`. 위치는 `ArenaGeometry` 에서 받으므로 판정과 어긋나지 않는다 |
| 4인 전용 아트 넣기 | 지금은 단색 도형이다. 씬에 넣는다면 `DuelOnly` 처럼 경기 종류별로 켜고 끈다 |

서버 · 클라이언트는 `ArenaLayout` 값이 같아야 하므로 판정 값을 바꾸면 서버와 Quest 를 함께 다시 빌드한다. 시야와 크기는 클라이언트 표시만 바뀐다.
