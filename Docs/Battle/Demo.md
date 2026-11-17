# 전시 시연 모드 — 규칙 · 실행 · 구현 위치

전시회 시연용. 메타 UI(로그인 · 로비 · 덱 · 매칭)를 건너뛰고 게임 플레이만 한다. `GameBootstrap.demoMode` (기본 켜짐, 명령줄 `-demo` · `-nodemo`).

## 실행

| 무엇 | 어떻게 |
| --- | --- |
| 서버 (PC) | `StartServer.bat` 더블클릭 — `Builds/Server/SpellboundServer.exe -server -demo` 를 창 모드로 켜고, 꺼지면 3초 뒤 다시 켠다. 로그 `Builds/Server/server_demo.log` |
| Quest | 앱 실행 → 연습 모드 → 끝나면 서버 경기에 자동 접속 (서버가 꺼져 있으면 켜질 때까지 계속 찾는다) |
| 서버 빌드 | Unity `Spellbound > Build > PC Server (Windows)` — `RunServerDemo.bat` 도 만든다. 예전 bat(`RunServer*.bat`)은 `-nodemo` |
| 예전 기능 | `-nodemo` 로 켜면 메타 UI · 4인 · 1:1 · 다인전 그대로. 웹 백엔드는 `Backend/StartBackend.bat` |

**스냅샷 크기가 바뀌었으므로 (헤더 `JoinSeat` · `JoinIn`, 플레이어 `Deaths`) 서버 · Quest 를 함께 다시 빌드해야 한다.**

## 흐름

```text
Quest 실행 ─▶ 연습 (이 기기 안 로컬 6인 전장, AI 는 서 있는 과녁)
               S03 파이어볼 → S04 화살비 → S11 도끼 전사, 스킬마다
               ① 눈앞 룬 가이드 따라 그리기(핀치) ② 주먹 ③ 검지로 가리켜 조준선 ④ 3초 유지 = 시전
           ─▶ 씬 다시 읽기 ─▶ 서버 접속 ─▶ 방 안 모두에게 "3초 뒤 새 플레이어가 N번 자리에" ─▶ 그 AI 자리를 이어받음
```

## 규칙 (`MatchRuleConfig.ApplyDemoRules`)

| 항목 | 내용 | 코드 |
| --- | --- | --- |
| 인원 | 6자리 고정. 사람이 없어도 서버가 빈자리를 바로 AI 로 채워 경기가 늘 돈다 | `NetworkMatchState.TickBotFill` |
| 입장 | 사람이 접속하면 예약되지 않은 AI 자리 하나를 고르고 `JoinNoticeSeconds`(3초) 동안 입장 알림 → 그 뒤 사람 차지. HP · 슬롯(로테이션) · 유닛 · 설치물 · 넥서스는 AI 가 쓰던 그대로. 알림 중 요청(시전 · 이동)은 받지 않는다. 6명 모두 사람이면 관전 | `MatchSimulation.TryReserveSeat` · `TickTakeovers` |
| 나감 | 사람이 나가면 AI 가 그 자리를 그대로 잇는다. 알림 중에 나가면 예약만 취소 | `NotifyDisconnected` |
| 체력 | 다인전(기본 3배)의 2배 = 6000 | `ApplyDemoRules` |
| 죽음 | 사람이 쓰러지면 그 사람은 **GAME OVER** (8초 뒤 앱이 연습부터 다시) 이고 자리는 AI 가 풀피로 잇는다. AI 는 그 자리에서 풀피 부활. 무너진 넥서스도 다시 선다 (유닛 · 슬롯은 그대로). 6각형은 줄지 않는다 | `TickDemoRespawn` · `OnLocalGameOver` · `GameOverView` |
| 경기 | 끝나지 않는다 — 결과 · 마나 폭풍 · 강화 · AFK 기권 없음 | `ApplyDemoRules` |
| 전장 | 가운데를 채운 정육각형 — 모든 두 자리 사이가 레인 (대각선도 소환 · 설치 · 웨이브). 레인 선은 평소엔 숨기고 그 레인을 조준하는 동안만 보인다 | `ArenaGeometry.FilledPolygon` · `FfaArenaView.BuildFilledFloor` |
| 조준 | 가리킨 방향에 가장 가까운 상대 레인에 조준선. 소환수 · 설치물(터렛 · 소환탑)은 그 선 위 가리킨 깊이에, **조준선의 절반까지** | `PlacementMaxDepth` · `ColumnDepthResolver` · `SummonExecutor` · `StructureExecutor` |
| 미니언 | 근접만. 웨이브(15초)마다 상대 5명 각 레인에 1마리씩. 원거리 · 대형 없음. 진영 생존 상한 28 (6 × 28 ≤ 유닛 풀 176) | `WaveSpawner` |
| 스킬 | 19종 모두. 섞어서 4장은 스킬창, 15장은 대기열 — 쓴 스킬은 대기열 맨 뒤로, 맨 앞 스킬이 그 칸에 들어와 `RotationRefillSeconds`(1.5초) 뒤 Ready. 19번 쓰면 19종을 모두 써 본다 (고위력 횟수 제한 없음). 접속한 Quest 가 보낸 덱은 무시 | `StartRotation` · `RotateSlot` · `RotationQueue` |
| AI | 랜덤 두뇌만 (VLM · 로컬 모델 AI 는 삭제): Ready 칸 중 랜덤 스킬 · 랜덤 상대(쓸 수 있는 열만) · 랜덤 깊이, 그리기 성공률 60% (`CastFailChance` 0.4), 랜덤 시간(5 ~ 20초)마다 랜덤 칸 이동 | `TimerRandomBrain` · `BotPlayer` |

## 화면

- **스킬창** (`BattleHudView.demoLayout`): 큰 카드 4장 한 줄 (룬 그림 약 10cm), HUD 폭 0.7m · 눈 아래 57°. 방어 줄은 뺐다. 서버가 칸 스킬을 바꾸면 카드를 다시 만든다 (`OnSlotSpellChanged`).
- **룬 가이드** (`SkillGuideView`): 스킬창을 내려다보면 (스킬창이 시선 30° 안) 눈 → 검지 끝 방향이 닿는 곳에 포인터가 뜨고 가장 가까운 카드가 커진다. 그대로 핀치하면 그 룬의 실루엣(굵고 흐린 흰 획 + 획 시작 초록 점 · 번호)을 눈앞 0.75m 에 크게 띄운다 — 받아쓰기처럼 따라 그린다. 게임 중엔 실루엣만, 연습 때는 실루엣 아래에 그리는 법 설명. 그 룬이 인식되거나 · 같은 카드를 다시 누르거나 · 15초 뒤 닫힌다. 10초 동안 아무 동작이 없으면 스킬창 카드 하나의 가이드가 저절로 뜬다. 카드를 고른 핀치는 그리기를 시작하지 않고, 내려다보지 않을 때의 핀치는 언제나 그리기다 (`CastStateMachine.PinchFilter`). 연습 중에는 카드 고르기를 끈다.
- **알림**: 다른 사람이 들어오기 3초 전 "새로운 플레이어가 N초 뒤 M번 자리에", 내가 들어가는 중이면 큰 "입장까지 N", 내가 쓰러지면 눈앞에 GAME OVER 판.
- **서버 옵저버** (`ServerSpectatorCamera.BeginObserver`): 1 ~ 6번 플레이어 등 뒤 9m · 높이 7.5m 에서 전장 가운데를 내려다보는 시점을 8초마다 돈다 (날아오는 스킬 · 다른 상대가 같이 보인다). 숫자 1 ~ 6 = 그 사람 · Tab = 다음 · O = 자동 순환 켜기/끄기 · V = 전체 보기 · 휠 = 확대. 입장 알림도 화면 위에.

## 검사

```text
dotnet run -c Release -- demotest   # Tools/BotLab — 사람 없이 시작 · 채운 육각형 5레인 웨이브 · 소환/설치 절반 깊이 · 10분 끝나지 않음 ·
                                    #   부활 · 근접만 · 로테이션 4칸 · AI 자리 넘겨받기(HP · 슬롯 · 유닛 유지) · 나가면 AI · 6명까지
```

## 남은 일 · 헤드셋에서 볼 것

- 채운 육각형 바닥 · 성채 섬 · 스킬창 크기 · 룬 가이드 위치(눈앞 0.75m)는 실행 중 생성이라 헤드셋에서 직접 보고 맞춘다.
- 카드 누르기 판정(손끝 8cm · 둘레 2cm)은 실제 손으로 맞춘다 (`SkillGuideView.touchDistance` · `touchPad`).
- 플레이어 HP 6000 (다인전 3배 × 2) — 10분 봇전에 쓰러짐 1번. 너무 오래 걸리면 `ApplyDemoRules` 의 배율을 낮춘다.
- S09 타워 강화는 시연 전장에 타워가 없어 시전만 되고 효과가 없다 (로테이션에서 쓰면 다음 스킬로 넘어간다).
