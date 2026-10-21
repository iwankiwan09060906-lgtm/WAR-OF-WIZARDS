# War of Wizards 웹 화면 · 서버 연결 설명서

웹 화면(로그인 → 로비 → 매칭 → 전투 → 결과)은 `Backend/static/` 의 HTML · CSS · JS 파일이고,
같은 폴더를 `Backend/wow_server.py` 하나가 함께 내보냅니다. 따로 빌드할 것은 없습니다.

## 1. 실행

```bat
cd Backend
run_server.bat              :: 또는  python wow_server.py  (포트를 바꾸려면 python wow_server.py 8090)
```

- 웹 화면: http://localhost:8080/
- 관리자 콘솔: http://localhost:8080/#/admin (관리자 계정으로 로그인)
- 시험 계정: 데모 `demo@wow.dev / demo1234`, 관리자 `admin@wow.dev / admin1234`
- 서버 확인: http://localhost:8080/api/health

| 환경 변수 | 뜻 | 기본값 |
|---|---|---|
| `WOW_DB_FILE` | 쓸 SQLite 파일 (시험용 DB 를 따로 쓸 때) | `Backend/wow_database.sqlite` |
| `WOW_UNITY_SPELL_UDP` | 시전 신호를 받을 Unity 주소 | `127.0.0.1:5067` |
| `GOOGLE_CLIENT_ID` | Google 로그인 클라이언트 ID | 없음 (아래 3번) |

시험: `python Backend/test_web_ui_api.py` (임시 DB 복사본으로 서버를 띄워 57개 항목 검사)

## 2. 화면과 파일

| 주소 | 화면 | 파일 |
|---|---|---|
| `#/login` · `#/signup` | 로그인 · 가입 (게스트 · Google · 데모) | `client/pages/auth.js` |
| `#/lobby` | 메인 로비 (1:1 마법 대전 카드 + 메뉴 6칸) | `lobby.js` |
| `#/mypage` | 전적 · 승률 · 닉네임/비밀번호 변경 | `mypage.js` |
| `#/settings` | 환경설정 (브라우저에 저장) | `settings.js` |
| `#/friends` | 친구 요청 · 접속 중 · 초대 | `friends.js` |
| `#/room` | 친선전 룸 (코드 6자리, 봇 채우기, 준비 · 시작) | `room.js` |
| `#/mailbox` | 우편 · 공지 · 초대장 | `mailbox.js` |
| `#/ranking` | 랭킹 상위 100명 | `ranking.js` |
| `#/spellbook` | 마법서 · 덱 8장 편성 | `spellbook.js` |
| `#/queue` → `#/countdown` → `#/battle` → `#/result` | 랭킹전 매칭 → 3·2·1 → 전투 HUD → 결과 | `queue.js` `countdown.js` `battle.js` `result.js` |
| `#/runes` | 룬 연습장 (서버 룬 인식 시험, Unity 로 시전 신호) | `runes.js` |
| `#/admin` | 관리자 콘솔 (현황 · 사용자 · 게임 설정 · 공지/방송 · 기록) | `admin.js` |
| 없는 주소 | 404 안내 (`404.html`, 앱 안에서는 `notfound.js`) | |
| 서버가 꺼졌을 때 | 500 안내 + 5초마다 자동 재연결 (`50x.html`) | |

- 공통: `client/core.js` (서버 연결 · 상태 · 머리글 · 마법사 그림), `client/main.js` (화면 이동), `client/wow.css` (디자인 전체)
- 서버 호출은 모두 `app/wow-server-api.js` 를 거칩니다. 로그인 토큰은 브라우저 `localStorage` 의 `wow.spring.token` (Unity WebGL 과 공유).
- 내 컴퓨터(localhost)에서 열면 왼쪽 위에 **DEV** 화면 목록이 나옵니다. 매칭 · 카운트다운 · 전투 화면에서는 숨고, `` ` `` 키로 꺼낼 수 있습니다.
- 예전 화면 파일 주소(`/lobby.html`, `/admin.html` 등)는 새 화면(`/#/lobby`)으로 자동 이동합니다.
- 스킬 그림은 `assets/skills/{rune,rune_line,circle}/<코드>.png` 파일만 바꾸면 모든 화면에 반영됩니다.

## 3. Google 로그인 켜기

1. https://console.cloud.google.com/ → 프로젝트 만들기 → **API 및 서비스 → 사용자 인증 정보 → OAuth 클라이언트 ID** (웹 애플리케이션)
2. **승인된 자바스크립트 원본**에 `http://localhost:8080` 추가 (다른 주소로 열면 그 주소도)
3. 받은 클라이언트 ID(`….apps.googleusercontent.com`)를 다음 중 하나에 넣기
   - `Backend/google_client_id.txt` 파일 첫 줄 (`#` 로 시작하는 줄은 무시)
   - 환경 변수 `GOOGLE_CLIENT_ID`
   - 관리자 콘솔 → 게임 설정 → `auth.google_client_id`
4. 서버를 다시 켜면 로그인 화면의 "Google 계정으로 계속하기" 가 동작합니다. (ID 가 없으면 버튼이 설정 방법을 안내)

## 4. Unity 연결

### 4-1. 시전 신호 (웹 → 서버 → Unity)

웹에서 룬을 그려 인식되면 `POST /api/spells/cast` → 서버가 **UDP `127.0.0.1:5067`** 로 JSON 한 줄을 보내고, 같은 내용을 SSE `spell` 이벤트로도 뿌립니다.

```json
{"type":"spell","phase":"cast","spellId":"S06","spellNumber":6,"name":"번개","nameEn":"Lightning",
 "tier":"NORMAL","kind":"area","targeting":"...","color":"#...","circleImage":"/assets/skills/circle/S06.png",
 "runeImage":"...","runeLineImage":"...","column":1,"depth":0.5,"slot":2,
 "caster":"데모마법사","playerId":1,"source":"web","t":1760400000.123}
```

- `phase`: `ready`(슬롯 준비) · `cast`(발동) · `cancel`(취소)
- `column`: 0 왼쪽 · 1 가운데 · 2 오른쪽 (-1 = 조준 없음), `slot`: 0~7 (-1 = 없음)
- null 이 없는 평평한 JSON 이라 Unity `JsonUtility` 로 바로 읽습니다. 최근 신호 확인: `GET /api/spells/recent`

### 4-2. 실시간 이벤트 (SSE)

`GET /api/live/events?access_token=<토큰>` — 이벤트 `spell`(시전), `match`(게임 방 join · result · leave), 관리자 방송 등.

### 4-3. 게임 방 (매칭 후 Unity 가 쓰는 API)

매칭이 되면 서버가 게임 방(`matchId`)을 만듭니다. Unity 는 웹과 같은 토큰으로:

| 요청 | 하는 일 |
|---|---|
| `GET /api/match/current` | 지금 내 게임 방 (상대 · 모드 · 상태) |
| `POST /api/match/{id}/join` | Unity 접속 알림 (웹 HUD 의 "Unity 연결됨") |
| `POST /api/match/{id}/result` `{"result":"WIN"\|"LOSE"\|"DRAW","durationSec":..,"casts":..,"damageDealt":..,"damageTaken":..}` | 결과 기록 → 레이팅 반영, 웹은 결과 화면으로 |
| `POST /api/match/{id}/leave` | 나가기 |

### 4-4. Unity WebGL 빌드를 웹 전투 화면에 넣기

1. WebGL 빌드 결과물을 `Backend/static/game/` 에 넣습니다 (`game/index.html` 이 있어야 함).
2. 전투 화면 가운데가 자동으로 `game/index.html?matchId=…` iframe 으로 바뀝니다.
3. 주고받는 메시지 (`window.postMessage`, 같은 출처만)

| 방향 | 메시지 |
|---|---|
| 웹 → Unity | `{source:"wow-web", event:"match-start", data:"{mode,matchId,opponent,deck}"}` (iframe 로드 직후) |
| 웹 → Unity | `{source:"wow-web", event:"spell", data:"<4-1 과 같은 JSON>"}` |
| Unity → 웹 | `{source:"wow-unity", event:"ready"}` → 웹이 `/join` 호출 |
| Unity → 웹 | `{source:"wow-unity", event:"state", data:"{myHp,enemyHp,maxHp}"}` → HUD 체력바 |
| Unity → 웹 | `{source:"wow-unity", event:"match-end", data:"{result:'win'\|'lose', reason}"}` → 결과 화면 |

Unity 쪽 보내기는 `UnityClient/Assets/_Project/Backend/Plugins/WebGL/WowBridge.jslib` (C# 에서는 `WebGLBridge.SendToPage`) 를 쓰면 됩니다. (Unity 작업은 Unity 담당 팀원이 진행)

## 5. 디자인 규칙 (요약)

- 스팀 게임 클라이언트처럼 차분한 남색 판 + 깎은 모서리(사이버 느낌), 장식용 빛 번짐 · 격자 · 보라색 없음
- 색은 뜻에만: 초록 = 화면의 주요 행동 1개 (전투 슬롯은 Unity HUD 처럼 READY 초록), 청록 = 나 · 정보, 분홍빨강 = 상대 · 위험, 호박색 = 고위력 스킬
- 글꼴: 한글 Noto Sans KR, 숫자 · 코드 · 영문 Chakra Petch
- 모든 주요 화면에 파란 마법사(나) vs 빨간 마법사(상대) — 상대는 매칭 전까지 검은 실루엣
- 자세한 값은 `Docs/Backend/WebDesign.md`
