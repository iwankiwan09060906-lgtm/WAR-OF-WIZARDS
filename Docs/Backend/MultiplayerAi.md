# 개인전 2~4명 · 공식 팀 계정 · AI 전략 코치(VLM) · 서버 MediaPipe

백엔드(`Backend/wow_server.py`)에 추가한 기능과 API. **Unity 코드는 바꾸지 않았다** — 기존 1:1 응답 칸은 그대로 두고 새 칸만 더했으므로 지금 Unity 빌드는 그대로 동작한다. Unity 쪽에서 쓰려면 맨 아래 "Unity 담당에게"를 본다.

```text
        ①
      / | \        한 경기 2~4명, 팀 없음 — 나 빼고 모두 적
    ④ — + — ②      (2명 = 예전 1:1)
      \ | /        빈자리는 봇이 채운다
        ③
```

---

## 1. 실행

| 실행기 | 켜지는 것 |
| --- | --- |
| `Backend/run_server.bat` | 서버 전부 (외부 패키지 없음). AI 코치는 **규칙 AI**, 손 추적은 **랜드마크 입력만** |
| `Backend/run_server_ai.bat` | 위 + 서버 MediaPipe(카메라 프레임) + Claude VLM. 첫 실행 때 `GestureTools/MediaPipe/.venv` 에 `mediapipe` · `anthropic` 설치 |

Claude VLM 을 쓰려면 실행 전에 `set ANTHROPIC_API_KEY=키` (없으면 규칙 AI 로 답한다 — 게임은 멈추지 않음).

---

## 2. 개인전 2~4명

### 2.1 매칭 큐

| 요청 | 설명 |
| --- | --- |
| `POST /api/matchmaking/queue` `{ "players": 4 }` | 2 · 3 · 4 (없으면 2 = 1:1). `{"format":"ffa4"}` · `{"maxPlayers":4}` 도 된다 |
| `GET /api/matchmaking/status` | 같은 인원을 고른 사람끼리 레이팅 범위가 맞으면 모아서 매칭. `matchmaking.bot_timeout`(기본 15초)이 지나면 모인 사람만으로 시작하고 빈자리는 봇 |

탐색 중: `{"state":"searching","maxPlayers":4,"playersFound":2,"playersNeeded":4, ...예전 칸}`

매칭 완료 (예전 1:1 칸 + 새 칸):

```json
{"state":"matched","matchId":12,"roomCode":"RANK-3F9A1C","maxPlayers":4,"format":"ffa4","seat":1,
 "opponentNickname":"1조 · 3조 · Bot (1210)","opponentRating":1180,"opponentPlayerId":21,"bot":false,
 "opponents":[{"playerId":21,"nickname":"1조","rating":1200,"bot":false,"seat":0},
              {"playerId":23,"nickname":"3조","rating":1150,"bot":false,"seat":2},
              {"nickname":"Bot (1210)","rating":1210,"bot":true,"seat":3}]}
```

- `seat` 0~3 = 자리 번호 (먼저 기다린 사람이 0). `bot` = 다른 사람이 하나도 없는 경기면 true.
- 1:1 큐(인원 2)와 3 · 4인 큐는 섞이지 않는다.

### 2.2 게임 방 (`/api/match/{id}/join · result · leave`, `GET /api/match/current`)

- `join`: 사람 자리가 모두 들어오면 `PLAYING` (봇 자리는 처음부터 들어온 것으로 본다).
- `result`: `{"result":"WIN"|"LOSE"|"DRAW","place":1~4, durationSec, casts, damageDealt, damageTaken}` — 남아 있는 사람이 모두 보고하면 `ENDED`.
- `leave`: 시작 전이면 방 취소. 경기 중 1:1 은 취소(예전 그대로), 개인전은 **그 사람만 빠지고 경기는 계속** (사람이 모두 나가면 취소).
- `GET /api/match/current` 의 `match`: 예전 칸(`opponent` · `unity.opponent` · `opponentResult`) + `maxPlayers` · `seat` · `opponents`(나 빼고 전부) · `players`(자리 표 전체) · `place`.

**랭킹전 레이팅** (`rating.ranked_win` · `rating.ranked_lose` 기본 16)

| 인원 | 1등 | 2등 | 3등 | 4등 |
| --- | --- | --- | --- | --- |
| 2명 (1:1) | +16 | -16 | | |
| 3명 | +16 | 0 | -16 | |
| 4명 | +16 | +5 | -5 | -16 |

`place` 를 안 보내면 WIN = +16, LOSE = -16. 친선전 · 봇 연습은 기록만 한다.

### 2.3 친선전 방 (`/api/rooms`)

| 요청 | 설명 |
| --- | --- |
| `POST /api/rooms` `{ "players": 4, "code"? }` | 방 만들기 (방장 = 자리 1). 응답 `maxPlayers` |
| `POST /api/rooms/{code}/join` | 빈자리에 앉는다. 꽉 찼으면 봇 자리를 사람에게 내준다, 봇도 없으면 400 |
| `POST /api/rooms/{code}/ready?ready=true` | 게스트 준비 |
| `POST /api/rooms/{code}/bot` | 방장: 빈자리에 봇 |
| `POST /api/rooms/{code}/kick` `{memberId}` · `{playerId}` | 방장: 내보내기 (빈 몸이면 첫 게스트 — 예전 1:1) |
| `POST /api/rooms/{code}/start` | 방장: 3 · 4인 방은 모든 게스트가 준비해야 시작, 빈자리는 봇, **서버가 게임 방을 만들고 `matchId` 를 돌려준다**. 1:1 방은 예전처럼 상태만 바꾼다 |
| `GET /api/rooms/{code}` | 예전 칸(host · guest = 첫 게스트) + `maxPlayers` · `members`(자리 표) · `matchId` |

---

## 3. 공식 팀 계정

| 아이디 (웹) | 비밀번호 | 이메일 (Unity · Quest 로그인 화면) |
| --- | --- | --- |
| 1조 | 1111 | group1@wow.dev |
| 2조 | 2222 | group2@wow.dev |
| 3조 | 3333 | group3@wow.dev |
| 4조 | 4444 | group4@wow.dev |

- 서버가 처음 켜질 때 자동으로 만든다 (`GROUP_ACCOUNTS`). 네 계정은 처음부터 서로 친구 → 4인 친선전 방에 바로 초대할 수 있다.
- `POST /api/auth/login` 의 `email` 칸에 이메일 **또는 아이디**(1조 · 닉네임)를 넣으면 된다 (`{"id":"1조"}` 도 됨). 응답에 `official: true` · `loginId`.
- 웹 로그인 화면: 아이디 칸 + "공식 팀 계정 1조 2조 3조 4조" 바로 시작 버튼.
- Unity 로그인 화면은 `@` 가 있어야 하고 VR 키보드에 한글이 없어서 **이메일**로 로그인한다.

---

## 4. AI 전략 코치 — `POST /api/ai/strategy`

지금 상황(+게임 화면 사진)을 보고 "무슨 스킬을 어느 열에, 어디로 움직일지, 개인전이면 누구를 노릴지"를 돌려준다.
로그인 토큰 또는 `X-Server-Key`(Unity 서버 · 봇, 봇마다 `botId`).

```json
{ "state": { "deck": ["S03","S04","S08","H01"], "SelfSlotReadyMask": 11, "SelfHp": 820, "SelfColumn": 1,
             "MatchTimeSeconds": 75, "OpponentTowerAlive": true, "IncomingProjectileCount": 1,
             "NearestIncomingProjectileColumn": 1, "NearestIncomingProjectileEta": 0.8,
             "lanes": [{"allies":1,"enemies":4},{"allies":2,"enemies":1},{"allies":0,"enemies":0}],
             "players": 4, "opponents": [{"nickname":"2조","hp":300,"column":2,"towerAlive":false}] },
  "image": "data:image/jpeg;base64,...(선택)", "provider": "rules|claude|ollama (선택)", "botId": 7 }
```

- `state` 는 Unity `BotObservation` 을 그대로(PascalCase) 보내도 되고 웹처럼 camelCase 여도 된다. 열은 모두 **자기 시점** (0 왼쪽 · 1 가운데 · 2 오른쪽).
- 응답: `summary` · `threat`(low/medium/high) · `plan[]` · `cast {slot, spellId, column, depth, reason}` · `move {direction -1/0/1, reason}` · `focus`(노릴 상대, 1:1 이면 빈 문자열) · `observations[]`(그림에서 본 것) · `source`(claude/ollama/rules) · `fallbackReason`.
- VLM 답은 서버가 한 번 더 고친다: 쿨타임 중인 슬롯 · 덱 밖 스킬 · 벽 밖 이동은 규칙 AI 답으로 바꾼다.

**설정** (관리자 콘솔 `#/admin` → 게임 설정, 바로 반영)

| 키 | 기본값 | 뜻 |
| --- | --- | --- |
| `ai.vlm_provider` | auto | auto = Claude 를 쓸 수 있으면 Claude, 아니면 규칙 AI · claude · ollama · local(이 게임으로 학습한 VLM) · rules |
| `ai.vlm_model` | claude-opus-5-5 | Claude 모델 ID (빠르게: claude-haiku-5-5) |
| `ai.vlm_effort` | low | 생각 깊이 low · medium · high (낮을수록 빠름) |
| `ai.ollama_url` · `ai.ollama_model` | http://127.0.0.1:11434 · qwen2.5vl:7b | 무료 로컬 VLM (`ollama pull qwen2.5vl:7b`) |
| `ai.vlm_min_interval` | 3 | 같은 플레이어의 VLM 요청 최소 간격(초) — 유료 API 남용 방지, 그 사이는 규칙 AI |

**학습**: 판 결과로 배운 것(이 상황에서 이긴 스킬 · 이긴 판 예시)이 규칙 AI 와 VLM 에 함께 들어간다 → [`AiLearning.md`](AiLearning.md).

Claude 는 공식 `anthropic` SDK 로 부르고(structured outputs 로 JSON 고정), 안전 분류기 거절에 대비해 서버 쪽 fallback(`fallbacks: "default"`)을 켜 두었다. **Claude 는 요청마다 요금이 나간다.**

웹 전투 화면: 왼쪽 "AI 전략 코치" — `AI 전략` 버튼, `화면 보여주기`(Unity 게임 창을 화면 공유하면 VLM 이 그 화면을 본다), `추천대로 쓰기`.

---

## 5. 서버 MediaPipe 손 추적 — `POST /api/mediapipe/frame`

`GestureTools/MediaPipe/wow_hand_tracker.py` 와 같은 손 자세 로직 · 룬 인식기를 서버에서 돌린다. 플레이어마다 따로(4명 동시 가능).

```json
{ "image": "data:image/jpeg;base64,...", "t": 12345.6, "candidates": ["S03","S06"], "session": "battle",
  "mirror": true, "forward": false }
```

- `image` 대신 `hands: {"right": [[x,y,z]*21], "left": ...}` (MediaPipe JS 등이 뽑은 랜드마크)도 된다 — 이건 mediapipe 없이도 동작.
- 응답: 손 추적기 UDP `frame` 메시지와 같은 모양 (`right` · `left`: tracked · pose · landmarks · cursor · column · depth) + `events[]` (`pinch_start` · `pinch_end`(+`rune`) · `fist` · `point` · `palm_open` · `move` · `tracking`) + `ms`(서버 처리 시간).
- `forward: true` 면 같은 메시지를 Unity `MediaPipeHandInput` UDP(`mediapipe.unity_udp`, 기본 127.0.0.1:5065)로도 보낸다.
- `GET /api/mediapipe/status` — 카메라 프레임을 쓸 수 있는지 (`image.available`, 안 되면 이유), `POST /api/mediapipe/reset {session}`.
- 웹 전투 화면 오른쪽 "웹캠 손 추적" — 핀치로 룬 그리기 → 검지로 열 가리키기, 손바닥 = 취소, 왼손 휘두르기 = 이동.

---

## 6. 검사

```text
python Backend/test_web_ui_api.py                     # 110개 (1:1 예전 검사 · AI 학습 포함)
set WOW_TEST_PYTHON=GestureTools\MediaPipe\.venv\Scripts\python.exe   # 이렇게 하면 카메라 프레임(MediaPipe)까지
```

---

## 7. Unity 담당에게 (Unity 는 손대지 않았음)

지금 Unity 빌드는 그대로 1:1 로 동작한다. 개인전 · AI 를 Unity 에서 쓰려면:

| 할 일 | 내용 |
| --- | --- |
| 매칭 인원 | `BackendSessionService.RequestMatchmaking` 이 `/api/matchmaking/queue` 에 `{"players":4}` 를 보내게 (지금은 빈 몸 = 1:1) |
| DTO | `MatchmakingStatusDto` 에 `int maxPlayers; int seat; MatchOpponentDto[] opponents;` (`MatchOpponentDto { long playerId; string nickname; int rating; bool bot; int seat; }`), `MatchSessionDto` 에 `maxPlayers · seat · place`, `MatchResultRequestDto` 에 `int place` |
| 친선전 방 | `RoomCreateRequestDto` 에 `int players`, `RoomDto` 에 `int maxPlayers; long matchId; RoomMemberDto[] members;` — 3 · 4인 방은 `start` 응답의 `matchId` 로 게임 방에 들어간다 |
| 전투 | 지금 시뮬레이션은 Home/Away 2명 고정 (`TeamUtil.TeamCount = 2`, PlayerId 1 · 2). 4명 개인전 전장 배치 · Fusion 세션 인원은 Unity 쪽 설계 |
| 봇 두뇌 | `IBotBrain` 구현에서 `BotObservation` 을 `/api/ai/strategy` 로 보내고(`X-Server-Key`, `botId`) 응답 `cast` · `move` 를 `BotCommand` 로 — 1~3초에 한 번 (`ai.vlm_min_interval`) |
| 로그인 | 조 계정은 이메일 `group1@wow.dev` ~ `group4@wow.dev` 로 (로그인 화면이 `@` 를 요구하고 VR 키보드에 한글이 없음) |

---

## 8. Tomcat 에서 돌릴 수 있나?

`wow_server.py` 는 파이썬 서버라 **Tomcat(자바 서블릿 컨테이너) 안에서는 직접 못 돈다.** 방법:

1. **그대로 파이썬 서버로 운영** (권장) — `run_server.bat` / `run_server_ai.bat`. MediaPipe · VLM 이 파이썬이라 이게 제일 단순하다.
2. **웹 화면만 Tomcat** — `Backend/static` 폴더를 Tomcat `webapps/ROOT` 에 올리고, `index.html` 에서 `window.WOW_SERVER_URL = "http://서버IP:8080"` 로 API 를 파이썬 서버에 보낸다 (서버가 CORS `*` 를 이미 허용).
3. **자바로 다시 만들기** — 예전 `feat/web-ai-backend` 브랜치의 Spring Boot 서버를 WAR 로 만들어 Tomcat 에 올릴 수 있지만, 이번 개인전 · 팀 계정 · AI API 는 파이썬에만 있어 옮겨야 하고, MediaPipe · VLM 은 결국 파이썬을 따로 불러야 한다.
