# AI 전략 봇 (AiStrategyBrain) — 랜덤 봇 대신 전장을 보고 두는 봇

§20.4 "FSM 봇" 자리에 넣은 두뇌. 기존 `TimerRandomBrain`(정해진 간격마다 랜덤 슬롯 · 랜덤 조준)을 대신해 **기본 봇**이 된다.
계약서(`IBotBrain` · `BotObservation`)는 바꾸지 않았다.

## 바뀐 Unity 파일

| 파일 | 내용 |
| --- | --- |
| `Bot/Brains/AiStrategyBrain.cs` (새) | AI 전략 두뇌 + 가중치(`AiBrainWeights`) + 서버 연결 인터페이스(`IStrategyAdvisor`) |
| `Bot/BotConfig.cs` | `Brain`(기본 `AiStrategy`, `TimerRandom` 이면 예전 봇) · `AiCastGapSeconds`(2.5초) · `UseServerStrategy` · `ServerAdviceInterval` · `ReportExperience` |
| `Bot/BotPlayer.cs` | 설정대로 두뇌를 만들고(`CreateBrain`), 틱마다 두뇌에게 전장(레인 유닛)을 보여 준다 |
| `Backend/AiStrategyLink.cs` (새) | 서버 AI 연결 — 씬에 붙일 필요 없음 (게임 시작 때 스스로 생김) |

기존 `BotConfig.asset` 은 새 칸이 없어도 코드 기본값(AI 전략 두뇌)으로 읽힌다. 예전 랜덤 봇으로 돌리려면 그 에셋의 `Brain` 을 `TimerRandom` 으로.

## 어떻게 두나

1. **피하기** — 내 칸으로 오는 투사체가 1.4초 안에 닿으면 옆 칸으로 (가운데 쪽 우선).
2. **시전** — 쓸 수 있는 슬롯마다 지금 가치를 매겨 가장 높은 것 하나. 사람처럼 시전 사이에 2.5초쯤 쉰다.
   - 레인마다 적 · 아군 유닛 수, 가장 가까운 적의 깊이, 적이 가장 많이 겹친 깊이, 내 타워 앞까지 온 적 수를 본다.
   - 독 장판은 같은 깊이에 걸친 적 전체, 소환은 밀리는 레인, 터렛 · 소환탑은 위험한 레인, 프리즈는 적이 몰렸을 때,
     레이저 · 흡혈은 상대 타워가 무너져 피해 100% 일 때 상대가 선 열로, 쉴드 · 반사는 HP 가 낮을 때.
   - 고위력(경기당 3회)은 가치가 확실히 클 때만.
3. **서버 전략** — 서버(판 결과로 배운 것 · VLM)가 최근 4초 안에 준 수가 있고 그 슬롯이 준비됐으면 그 수를 쓴다. 서버가 없으면 혼자 둔다.

## 승률 (Tools/BotLab — 실제 MatchSimulation 으로 봇끼리, 2026-10-26)

새 시드 500판씩, AI 는 판마다 Home / Away 를 번갈아 맡음.

| 대진 | AI 승률 |
| --- | --- |
| 랜덤 vs 랜덤 (기준) | 47.4% |
| **AI 전략 vs 랜덤** (기본 시전 간격 2.5초) | **99.0%** (494승 5패) |
| **AI 전략 vs 랜덤** (랜덤과 같은 시전 속도 6초 — 판단만 비교) | **90.3%** (449승 48패) |
| AI 전략 vs AI 전략 (기준) | 46.6% |

- 가중치는 손으로 정하지 않고 **측정으로 찾았다** (`BotLab tune`: 300판으로 후보마다 승률을 재서 오르는 것만 남김, 3바퀴).
  손으로 짠 첫 버전은 같은 시전 속도에서 랜덤에게 **39%** 였다 — 범위 피해로 웨이브만 지우느라 타워를 못 부쉈다.
- 찾은 것: 지금 밸런스에서는 레인 하나를 청소하는 피해(S07 총난사 · S04 화살 세례 · S06 번개 · S01 연막)보다
  **소환(S11 · S12)과 독 장판(S02)** 이 타워를 부수는 데 훨씬 낫다. 웨이브는 계속 나오므로 밀어 주는 유닛이 이긴다.
  → 이 네 스킬이 약하다는 **밸런스 참고**도 된다 (사람 플레이에서도 같은지 확인해 볼 것).

## 다시 재기 · 다시 찾기

```text
cd Tools/BotLab
dotnet run -c Release -- 500            # 대진 4개 × 500판 승률 (몇 초)
dotnet run -c Release -- diag 200 6     # AI vs 랜덤: 양쪽이 쓴 스킬 · 상대 타워를 부순 판 수
dotnet run -c Release -- tune 300 3     # 가중치 찾기 (약 13분) → tuned_weights.txt
```

`tuned_weights.txt` 를 `AiStrategyBrain.cs` 의 `TUNED-BEGIN` ~ `TUNED-END` 사이에 붙이면 Unity 봇에 적용된다.
스킬 수치(`SpellDefinition` 기본값 · 에셋)를 바꿨다면 다시 찾는 것이 좋다.

- BotLab 은 `UnityClient/Assets/_Project` 의 전투 · 봇 코드를 **복사하지 않고 그대로** 컴파일한다 (Unity 기능 몇 개만 `UnityShim.cs` 대역).
  에셋 대신 코드 기본값(`CreateDefault`)을 쓰므로 에셋으로 바꾼 수치와는 조금 다를 수 있다.
- 전투 코드가 정적 버퍼를 써서 판은 한 번에 하나씩 돈다 (Unity 와 같다).
- `Tools/BotLab/UnityCheck`: 같은 파일 + `AiStrategyLink` 를 **진짜 UnityEngine DLL**(설치된 에디터 6000.0)로 C# 9 컴파일 — 오류 0.

## 서버 연결 (AiStrategyLink)

- 전략: `POST /api/ai/strategy` (봇마다 3초에 한 번), 경험: `POST /api/ai/experience` (3초마다 묶어서), 경기 끝: 결과 이름표.
- 인증: `X-Server-Key` (서버 `WOW_SERVER_KEY`). 다른 키는 명령줄 `-wowServerKey 키`. 끄기: `-noAiLink` 또는 BotConfig 체크 해제.
- 서버가 3번 연속 안 받으면 30초 쉬고 다시 — 그동안은 봇 혼자 둔다.
- 서버가 이 데이터로 자동 학습 → 다음 전략 요청부터 반영 ([`Docs/Backend/AiLearning.md`](../Backend/AiLearning.md)).

## 확인하지 못한 것

- Unity 에디터에서 열어 보지 않았다 (프로젝트는 6000.3.10f1, 이 PC에는 6000.0 · 6000.5 만 있음). 처음 열면 새 파일의 `.meta` 가 생긴다 — 콘솔 오류가 없는지 확인.
- 사람 대 AI 봇 체감 난이도 — 너무 세면 `AiCastGapSeconds` 를 4~6초로 올리면 약해진다 (같은 속도에서도 랜덤보다는 셈).
