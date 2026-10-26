# AI 가 War of Wizards 를 배우게 하기 (VLM 학습)

AI 전략 코치(`/api/ai/strategy`, [`MultiplayerAi.md`](MultiplayerAi.md) 4장)가 **실제 판 결과로 이 게임을 배우도록** 한 구조다.
서버 코드: `Backend/wow_learn.py` (외부 패키지 없음) · `wow_server.py` · `wow_ai.py`. Unity 는 손대지 않았다.

```text
 ① 모으기        ② 이름표           ③ 배우기 (GPU 없이 · 자동)       ④ 쓰기
 상황 · 화면  →   판이 끝나면     →  "이 상황에서 이 스킬 → 이겼나"  →  규칙 AI 가 스킬을 바꾸고
 행동 · 보상      WIN · LOSE · 등수    learned_policy.json               VLM 프롬프트에 배운 것 + 이긴 판 예시
                                          │
                                          └→ ⑤ VLM 미세조정 (GPU · 데이터가 쌓인 뒤): 이긴 판 장면으로 Qwen2.5-VL LoRA
                                              → 학습한 VLM 을 ai.vlm_provider = local 로 연결
```

---

## 1. 데이터는 어디서 모이나

모두 `ai_experiences` 표(SQLite)와 `Backend/ai_data/frames/`(화면 사진)에 쌓인다. `Backend/ai_data/` 는 git 에 안 올라간다.

| 어디서 | 무엇을 | 언제 |
| --- | --- | --- |
| 웹 전투 화면 | 시전할 때마다 상황(덱 · 쿨타임 · HP · 내 위치 · Unity 가 보낸 상황) + 실제 행동(스킬 · 열 · AI 추천대로 썼는지) + **화면 보여주기를 켰으면 그 순간 화면** | 자동 |
| AI 전략 코치 | 물어본 상황 + (화면) + AI 가 추천한 수 | `/api/ai/strategy` 를 부를 때마다 |
| Unity · 봇 (할 일) | `BotObservation` + 그 틱의 행동 + 즉시 보상 | `POST /api/ai/experience` (아래 4장) |
| 판 결과 | 그 판의 모든 경험에 WIN · LOSE · 등수 이름표 | 게임 방 결과(`/api/match/{id}/result`), 봇 연습 · 1:1 친선전(`/api/matches` + `gameId`), 또는 `/api/ai/experience {gameId, outcome}` |

`POST /api/ai/experience` (로그인 토큰 또는 `X-Server-Key` + `botId`)

```json
{ "gameId": "m12", "source": "unity", "state": { "SelfHp": 820, "SelfColumn": 1, "...": "BotObservation 그대로" },
  "action": { "spellId": "S06", "slot": 2, "column": 0, "depth": 0.4, "move": 0, "followedAdvice": true },
  "reward": 35.0, "image": "data:image/jpeg;base64,...(선택)" }
{ "gameId": "m12", "steps": [ {"state": {...}, "action": {...}, "reward": 12}, ... ] }      ← 묶음 (최대 100)
{ "gameId": "bot-7-0003", "outcome": "WIN", "place": 1, "players": 4 }                    ← 판 결과 이름표
```

- `gameId` = 한 판 구분. 게임 방(matchId)이 있으면 `m{matchId}` (웹이 자동으로 그렇게 보낸다).
- `reward` = 그 행동의 즉시 보상 (예: 다음 3초 동안 준 피해 − 받은 피해). 없어도 된다 — 판 결과만으로도 배운다.

---

## 2. 1단계 학습 — 판 결과로 배우기 (GPU 없이, 지금 바로)

- 상황을 갈래로 나눈다: 1:1/개인전 · 경기 시간(초반/중반/후반) · 내 HP · 레인 압박(적 유닛 수) · 상대 타워 생존 · 날아오는 투사체.
- 갈래마다 스킬별로 **가치 = 판 결과(이김 +1 · 짐 −1, 개인전은 등수) + 즉시 보상 조금**을 평균 낸다. 표본이 적은 스킬은 0 쪽으로 당긴다(베이즈 평활).
- 결과 이름표가 붙은 경험이 `ai.train_every`(기본 20)만큼 늘 때마다 **자동으로 다시 학습**한다. 관리자 콘솔 `#/admin` → **AI 학습** 탭에서 "지금 학습"도 된다 (`POST /api/ai/train`, 관리자).
- 학습 결과(`Backend/ai_data/learned_policy.json`)는 다음 전략 요청부터 바로 쓰인다:
  - **규칙 AI**: 같은 상황 표본이 `ai.learn_min_samples`(기본 5)개 이상이고 학습 가치가 규칙 AI 의 수보다 확실히 높으면 그 스킬로 바꾼다 ("학습(12판): 이 상황에서 S06 승률 100% · 12번"). 피하기는 그대로.
  - **VLM** (Claude · Ollama · 학습한 VLM): 프롬프트에 `learned` — 이 상황에서 이긴 스킬 순위와 이긴 판 예시 3개를 넣는다.
  - 응답의 `learned` 칸에 무엇을 배웠는지 나온다.
- AI 학습 탭에서 모은 경험 · 판 · 화면 사진 수, 잘 이긴 스킬, **AI 추천대로 쓴 수와 직접 고른 수의 승률**을 볼 수 있다.
- `GET /api/ai/learning` — 같은 내용 (누구나).

> 판이 적으면 학습은 거의 영향이 없다 (표본 5개 미만은 규칙 AI 그대로). 많이 할수록 강해진다.

---

## 3. 2단계 — VLM 미세조정 (이 게임 화면을 읽도록 모델 자체를 학습)

> **이 PC에서 실제로 돌려 확인했다 (2026-10-26)** — 설치 → 가짜 데이터 47장면 학습 → 서빙 → 게임 서버 연결까지.
> 진짜 판 데이터로는 아직 학습하지 않았다 (모아야 한다).

이 PC: RTX 4060 노트북 (VRAM 8GB) · RAM 16GB → **Qwen2.5-VL 3B + 4bit QLoRA**. 모든 단계는 `Backend/ai_finetune/run_finetune.bat` 하나로 한다.

| 단계 | 명령 | 확인한 것 |
| --- | --- | --- |
| 설치 (한 번, 약 11GB) | `run_finetune.bat setup` | `.venv` 에 PyTorch 2.11 (CUDA 12.8) · LLaMA-Factory 0.9.5 · bitsandbytes, 모델 7.5GB. 다운로드가 멈추면 다시 실행하면 이어 받는다 |
| 설치 확인 학습 | `run_finetune.bat smoke` | 가짜 데이터 47장면 × 3바퀴 = **13분**, VRAM 약 **7.9GB / 8GB**, loss 2.6 → 1.1 |
| 진짜 데이터 학습 | 관리자 콘솔 AI 학습 → "데이터셋 만들기" → `run_finetune.bat train` | 결과 LoRA → `Backend/ai_finetune/saves/wow-vlm-lora` |
| 서빙 | `run_finetune.bat serve` (확인 학습 결과는 `serve smoke`) | OpenAI 호환 `http://127.0.0.1:8790/v1` (이 PC에서만), 4bit 로 올려 VRAM 약 2.5GB |
| 연결 | 관리자 콘솔: `ai.vlm_provider = local` | 게임 서버의 전략 요청에 학습한 모델이 그림까지 보고 답함 (`source: local`) |

- 데이터셋 (`Backend/ai_data/dataset/`): 이긴 판의 장면만 — system(게임 규칙) · user(`<image>` + 상황 + 규칙 AI 제안) · assistant(그때 실제로 한 수, 전략 JSON).
  실제 전략 요청과 같은 모양이어야 한다 (규칙 AI 제안을 빼고 학습하면 실제로 쓸 때 제안을 그대로 베낀다 — 확인함).
- 한 장면 = 글자 약 2,100 + 그림 약 260 토큰 → `cutoff_len: 3072` (2048 이면 정답 부분이 잘린다).
- **속도 한계**: 4bit 로 올린 모델은 초당 약 2토큰이라 한 번 답에 **약 20초**. 판 사이 분석 · 봇 학습 데이터용으로는 되지만 실시간 코치로는 느리다.
  실시간으로 쓰려면 학습한 모델을 GGUF 로 바꿔 llama.cpp 로 돌려야 한다 (다음 할 일).
- 포트 8000 은 이 PC에서 다른 프로그램(Manager)이 쓰고 있어 **8790** 을 쓴다.

데이터가 더 쌓이면 "데이터셋 만들기 → train" 을 다시 하면 된다 (모델이 계속 이 게임에 맞춰진다).

---

## 4. Unity 연결 (2026-10-26 — Unity 봇이 AI 로 둔다)

Unity 봇은 이제 랜덤이 아니라 **AI 전략 두뇌**로 두고, 서버와 이어져 있다 → [`Docs/Bot/AiStrategyBot.md`](../Bot/AiStrategyBot.md)

| 흐름 | 누가 | 내용 |
| --- | --- | --- |
| 전략 | `Backend/AiStrategyLink.cs` | 봇마다 3초에 한 번 `POST /api/ai/strategy` (상황 + 레인 유닛, `X-Server-Key`, `botId`) → 서버 학습 결과 · VLM 의 수를 봇이 씀 |
| 경험 | 〃 | 봇의 판단(상황 + 행동)을 3초마다 묶어 `POST /api/ai/experience {gameId, steps}` |
| 판 결과 | 〃 | 경기가 끝나면 `{gameId, outcome}` → 그 판 경험에 이름표 → 서버 자동 학습 |
| 남은 일 | Unity 담당 | 화면 사진 (Camera → RenderTexture → JPEG) 을 몇 초에 한 장 `image` 로 — VLM 미세조정의 핵심 데이터 · WebGL `state` 메시지에 레인 정보 |

---

## 5. 설정 (관리자 콘솔 → 게임 설정)

| 키 | 기본값 | 뜻 |
| --- | --- | --- |
| `ai.save_frames` | true | 경험과 함께 화면 사진 저장 |
| `ai.max_frames` | 5000 | 저장할 화면 사진 최대 수 |
| `ai.train_every` | 20 | 이름표 붙은 경험이 이만큼 늘면 자동 학습 (0 = 끔) |
| `ai.learn_min_samples` | 5 | 학습 결과로 스킬을 바꾸려면 필요한 같은 상황 표본 수 |
| `ai.vlm_provider` | auto | auto · claude · ollama · **local**(학습한 VLM) · rules |
| `ai.local_url` · `ai.local_model` | http://127.0.0.1:8790/v1 · wow-vlm | 학습한 VLM 서버 |

검사: `python Backend/test_web_ui_api.py` — 113개 (경험 · 이름표 · 학습 · 전략 반영 · 데이터셋 · 학습한 VLM 연결 · Unity 봇 연결 형식 포함).
