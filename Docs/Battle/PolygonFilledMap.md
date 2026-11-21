# 다인전 전장 변경 — 가운데를 채운 정N각형 + 스킬 가이드

브랜치 `feat/polygonArena`. feat/MVP 시연 빌드의 6인 전장 디자인을 일반 다인전에 옮겼다.
인원이 6 → 5 → 4 → 3 → 2 로 줄어드는 규칙(탈락 · 재구성 · 1:1 전환 · 마나 폭풍 · 경기 종료)은 그대로다.

```text
  이전 (가운데 심연)                    지금 (채운 바닥)
        0                                    0
    1 ·───· 4    ─── 변 = 소환 · 웨이브       1 ·───· 4    모든 선 = 소환 · 설치 · 웨이브
     \  ╳  /     ··· 대각선 = 직접 공격만      \ ╳ /      (바닥이 다각형 하나로 채워짐)
      2───3                                  2───3
```

## 왜

가운데가 뚫려 있어서 소환물 · 설치물 · 웨이브가 좌 · 우 이웃에게만 갈 수 있었다. 건너편 상대는 직접 공격으로만 칠 수 있었다.

## 규칙 변화

| 항목 | 이전 | 지금 | 코드 |
| --- | --- | --- | --- |
| 바닥 | 변 = 돌다리, 가운데 = 심연 · 룬 핵 | 정N각형 안을 돌 바닥 하나로 채움 (아래로 좁아지는 절벽) | `FfaArenaView.BuildFilledFloor` · `FilledMesh` |
| 소환 · 설치 레인 | 좌 · 우 이웃 변만 | 살아 있는 상대 모두 (대각선 포함) | `ArenaGeometry.IsSpawnLane` |
| 웨이브 | 좌 · 우 변으로만 | 살아 있는 상대 모두에게 레인마다 | `WaveSpawner` (`ColumnSlots` 만큼 돈다) |
| 소환 · 설치 위치 | 레인 앞쪽 고정 위치 | 조준선 위 가리킨 깊이, **조준선의 절반까지** | `PlacementMaxDepth` · `ColumnDepthResolver` · `SummonExecutor` · `StructureExecutor` |
| 레인 표시 | 변은 늘 보이고, 대각선 빛줄기는 조준할 때만 | 레인 선은 모두 숨겨 두고, 그 레인을 조준할 때만 보임 | `FfaArenaView.BuildLane` |
| 발판 | 1번 발판 복제 | 낮은 돌 원판 + 팀 색 테두리 | `FfaArenaView.CleanPad` |
| 진영당 유닛 상한 | 40 | **28** | `MatchRuleConfig.ApplyPolygonRules` |
| 2명 남음 | 1:1 전장 (레인 3개) | 그대로 (채운 바닥 대신 1:1 다리) | `PolygonDuelStage` |

유닛 상한을 낮춘 이유: 웨이브가 상대 모두에게 나와 유닛 수가 크게 늘었다. 6진영 × 40 = 240 은 유닛 풀(`MatchSnapshot.MaxUnits` = 176)을 넘으므로 28 (× 6 = 168) 로 낮췄다. feat/MVP 시연과 같은 값이다.

켜는 곳: `GameBootstrap.SetArenaMode` 가 다인전이면 `arenaLayout.Geometry.FilledPolygon = true`. 서버 · 클라이언트 모두 이 함수를 거치므로 둘 다 적용된다.

## 스킬 가이드 (`SkillGuideView`)

다인전 경기에서만 켜진다 (1:1 · 4인 개인전은 그대로).

- **Quest**: 스킬창을 내려다보면(시선 30° 안) 검지 끝 방향으로 포인터가 뜨고, 가장 가까운 카드가 커진다. 그대로 핀치하면 그 스킬의 큰 룬 실루엣이 눈앞에 뜬다 (흐린 흰 획 + 시작점 초록 점, 여러 획이면 번호).
- **PC**: 카드를 마우스로 클릭.
- 10초 동안 아무 동작이 없으면 스킬창 카드 하나의 가이드가 저절로 뜬다.
- 닫힘: 그 룬이 인식되거나, 같은 카드를 다시 고르거나, 15초가 지나면.
- 카드를 고른 핀치는 룬 그리기를 시작하지 않는다 (`CastStateMachine.PinchFilter`). 내려다보지 않을 때의 핀치는 언제나 룬 그리기다.
- HUD 하단 안내 문구: "가리키는 쪽 상대를 공격 · 소환 · 카드를 보고 핀치 = 그리는 법".

조정값은 `SkillGuideView` 인스펙터 필드에 있다: `lookDownDegrees` 30, `idleGuideSeconds` 10, `autoHideSeconds` 15.

## 바뀐 파일

| 파일 | 내용 |
| --- | --- |
| `Arena/ArenaGeometry.cs` | `FilledPolygon` · `PlacementMaxDepth`, `IsSpawnLane` 이 모든 상대 허용 |
| `Presentation/FfaArenaView.cs` | 채운 바닥 메시, 레인 선 숨김, 돌 원판 발판 |
| `Input/RightHand/ColumnDepthResolver.cs` | 소환 · 설치 조준 깊이 (절반까지) |
| `Input/RightHand/CastStateMachine.cs` | `PinchFilter` (카드 핀치는 그리기 아님) |
| `Spells/Executor/SummonExecutor.cs` · `StructureExecutor.cs` | 조준한 깊이에 소환 · 설치 |
| `Combat/Wave/WaveSpawner.cs` | 상대 모두에게 웨이브 |
| `Core/MatchRuleConfig.cs` | 다인전 유닛 상한 28 |
| `Core/GameBootstrap.cs` | 다인전에서 `FilledPolygon` 켜기 · `SkillGuideView` 붙이기 |
| `UI/C12_HUD/BattleHudView.cs` | 카드 포인터 · 강조 (`TryPointAtSlots` · `TryGetSlotOnRay` · `HighlightSlot` · `CanvasRect`), 안내 문구 |
| `UI/C12_HUD/SkillGuideView.cs` (새 파일) | 룬 가이드 |
| `UI/Common/UiKit.cs` · `UiTheme.cs` | `RuneSilhouette` (실루엣 스프라이트) |

## feat/MVP 에서 가져오지 않은 것

시연 전용 기능이라 뺐다:

- 무한 부활 (`DemoEndless`), 스킬 로테이션 (`SkillRotation`), AI 자리 넘겨받기, 연습 모드 (`PracticeGuide`), 게임 오버 화면, 관전 카메라 자동 순환, 시연용 큰 카드 4장 HUD
- 스킬 데미지 밸런스 수정 (Fireball 160 → 330 등), 미니언 이동 속도 상향 (AxeOrc 1.5 → 2.4 · Dragon 1.8 → 2.8)

미니언 속도는 레인이 길어진 대각선 때문에 MVP 에서 올렸을 수 있다. 플레이해 보고 웨이브가 너무 느리면 가져온다.

## 확인 상태 · 남은 일

- [x] Unity 런타임 + Editor 코드 타입 체크 (에러 0)
- [ ] 에디터 / Quest 실제 플레이 — 6 → 2명까지 줄어들 때 바닥이 다시 지어지는지
- [ ] 2명 남은 1:1 단계 발판 모양 (돌 원판으로 보이는데 어색하지 않은지)
- [ ] BotLab `polytest` 는 `FilledPolygon` 을 켜지 않아 이번 변경을 검사하지 않는다 — 필요하면 테스트에 켜는 경우를 추가
- [ ] `Docs/Battle/Polygon.md` 의 소환 · 설치 · 웨이브 · 표시 항목은 이전 규칙 기준이다 — 이 문서로 갱신 필요
- [ ] 스냅샷 · 네트워크 필드는 바뀌지 않았지만 규칙(유닛 상한 · 레인)이 바뀌었으므로 서버 · Quest 를 함께 다시 빌드할 것
