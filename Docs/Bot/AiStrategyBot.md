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
3. **하이브리드 (Unity 두뇌 + VLM)** — 아래 "하이브리드 봇". 서버가 없으면 혼자 둔다.

## 하이브리드 봇 — 봇 하나 = Unity 두뇌(빠른 반사) + VLM(전략) (2026-10-29)

```text
        Unity (봇마다 하나, 0.25초)                         서버 wow_server.py (봇마다 따로 요청, 3초)
 ┌────────────────────────────────────┐   botPick 후보 3개  ┌──────────────────────────────────────────┐
 │ 피하기 (투사체 1.4초 안)            │   + 유닛 점 · 레인  │ 전장 지도 그리기 (wow_board.py, 사진 없을 때)│
 │ 후보 매기기 (가중치 = BotLab tune)  │ ─────────────────▶ │ 힌트 = Unity 두뇌 1순위 + 규칙 + 학습       │
 │ 작전 곱하기 (prefer ×1.6 · 레인 ×1.3 │                    │ VLM (llama.cpp Qwen2.5-VL · Claude · 학습한 │
 │   · hold 는 안 씀 · 자세 가중치)     │ ◀───────────────── │   모델) → 작전(orders) + 한 수 (brief)      │
 │ 위급(점수 ≥ 6)이면 두뇌가 먼저       │   orders + cast     │ VLM 이 바쁘거나 실패하면 힌트 작전           │
 │ 아니면 VLM 의 한 수(요청 5초 안)     │                    │ 판 결과 → 학습 → 다음 요청부터 반영          │
 └────────────────────────────────────┘                    └──────────────────────────────────────────┘
```

- **작전(orders)**: `lane` 집중 레인 · `stance` defend / balanced / push · `prefer` 먼저 쓸 스킬 · `hold` 아낄 스킬 · `seconds` 3~15초.
  두뇌가 유지 시간 동안 **0.25초마다** 점수에 곱한다 → VLM 답이 몇 초 걸려도 전략은 두뇌 속도로 반응한다 (빠른 반사 + 빠른 전략).
  BotLab tune 이 0 으로 만든 스킬도 VLM 이 `prefer` 로 고르면 다시 쓴다 (학습 · 탐색).
- **우선순위**: 피하기 → 위급(두뇌 점수 ≥ `BotConfig.EmergencyScore` 6) → VLM 의 한 수(요청한 지 5초 안 · 슬롯 준비) → 두뇌(작전 반영).
  VLM 이 정한 작전이 살아 있는 동안 규칙 작전(VLM 이 바쁠 때의 답)으로 덮어쓰지 않는다.
- **봇마다 따로**: 봇 하나 = 두뇌 하나 + 서버 요청 하나(`botId` 마다 요청 간격 · 작전 · 학습 기록이 따로). 봇끼리 정보를 나누지 않는다 (개인전).
  같은 VLM 서버를 같이 쓰므로 봇이 많으면 GPU 에서 차례로 처리한다 → 3봇이면 `llama-server --parallel 3`.
- 바뀐 파일: `Bot/Brains/AiStrategyBrain.cs` (BotView · BotCandidate · BotUnitDot · StrategyOrders, IStrategyAdvisor 에 TryGetOrders),
  `Bot/BotConfig.cs` (EmergencyScore), `Backend/AiStrategyLink.cs` (botPick · units · brief 요청, orders 받기),
  서버 `wow_ai.py` · `wow_board.py`(새) · `wow_learn.py`, BotLab `RemoteAdvisor.cs`(새) · `Program.cs vlm`.

### 밸런스: 소환(S11 · S12) 쿨타임 5초 → 8초 (2026-10-29)

VLM 봇끼리 12판 학습에서 소환이 가장 강했다 (S11 승률 71% · S12 58%). `SpellDefinition.CooldownOverride`(새 칸, 0 = 등급 기본값)를 만들어
S11 · S12 만 8초로 했다 (코드 기본값 `SummonCooldown` · 에셋 `Spell_S11_AxeWarrior` · `Spell_S12_Mage`, 5분 강화 감소는 그대로).
서버 `SKILLS` · 웹 `core.js` 도 8초. BotLab 300판씩:

| 대진 | 5초 | 8초 |
| --- | --- | --- |
| AI vs 랜덤 | 99.0% | 99.3% |
| AI vs 랜덤 (같은 시전 속도) | 90.5% | 89.2% |
| AI vs AI 평균 경기 · 시간 초과(900초) | 266초 · 14% | **426초 · 23%** |

소환이 타워를 부수는 주된 수단이라 쿨타임을 늘리면 봇끼리 판이 길어진다. 가중치(`BotLab tune`)와 서버 학습은 5초 때 판으로 만든 것이라
새 쿨타임으로 판을 더 모아 다시 학습 · 조정하는 것이 좋다.

### 하이브리드 봇끼리 대전 (진짜 서버 · VLM)

```text
cd Tools/BotLab
dotnet run -c Release -- vlm 10 http://127.0.0.1:8091      # 서버 주소 (ai.vlm_min_interval = 0 권장)
```

BotLab 은 경기를 빨리 돌리므로 VLM 답을 기다리는 동안 경기 시간이 멈추고, **실제로 걸린 시간만큼 경기 시간이 흐른 뒤에** 그 답을 쓴다
(실제 게임과 같은 지연). 판이 끝나면 양쪽 판단을 서버 학습 데이터로 보낸다 (`/api/ai/experience`, 이름표 포함).

2026-10-29 결과 (llama.cpp Qwen2.5-VL-3B, 5초 쿨타임 때): **A 4승 · B 6승** (같은 봇이라 50% 근처가 정상), 평균 경기 173초 · 시간 초과 0,
VLM 답 727번 평균 3.2초 · 실패 0, 시전의 약 54% 가 작전을 반영, VLM 의 한 수를 그대로 쓴 시전은 약 3% (답이 오는 사이 두뇌가 먼저 시전한다).

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
