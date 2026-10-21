# 백엔드 통합 안내

팀원 브랜치 `feat/web-ai-backend`의 백엔드(Python 서버 · 웹 UI · MediaPipe 손 추적)를 이 저장소에 합친 결과입니다.
Unity 게임 코드(전투 · 입력 · 네트워크 · 스킬)는 건드리지 않았고, 백엔드 영역만 추가했습니다.
팀원 원본 폴더 없이도 전부 동작합니다.

기획서 §2.1 · §23 의 **Supabase 자리를 이 Python 서버가 대신합니다.** (`Supabase/` 폴더는 비어 있는 채로 둠)

---

## 1. 폴더 구조

```text
WAR-OF-WIZARDS/
├── Backend/                         ★ 백엔드 서버 (Python 3.10+, 외부 패키지 없음)
│   ├── wow_server.py                  - 서버 하나에 전부: 인증 · 덱 · 전적 · 매칭 큐 · 친선전 · 친구 · 우편 · 랭킹 · 관리자
│   ├── run_server.bat                 - 더블 클릭으로 실행 (http://localhost:8080)
│   ├── open-firewall.bat              - Quest/다른 PC 에서 접속할 때 8080 포트 열기 (관리자 권한, 한 번만)
│   ├── test_web_ui_api.py             - 자동 검사 59개 (임시 DB 로 서버를 따로 띄움)
│   ├── test_server.py                 - 실행 중인 서버 간단 확인
│   └── static/                        - 웹 화면 (로그인 → 로비 → 덱 → 매칭 → 결과, 관리자 콘솔 #/admin)
│
├── GestureTools/MediaPipe/          ★ 웹캠 손 추적 → UDP → Unity (헤드셋 없이 시연용)
│   ├── run_hand_tracker.bat           - 첫 실행 때 .venv 에 MediaPipe 자동 설치
│   ├── wow_runes.py                   - 19종 룬 인식기 (서버의 /api/runes/recognize 도 이 파일을 씀)
│   └── UnityReceiver/MediaPipeHandInput.cs - IPlayerInput 구현 참고본 (Assets 밖이라 컴파일 안 됨)
│
├── UnityClient/Assets/_Project/Backend/   ★ Unity ↔ 서버 연결 (기획서 §28 이름 그대로)
│   ├── BackendClient.cs               - HTTP 공통 (서버 주소, 로그인 토큰, 접속 하트비트)
│   ├── AuthService · ProfileService · DeckService · MatchResultService
│   ├── RankingService · FriendService · PresenceService · MailService · NoticeService
│   ├── BackendSessionService.cs       - 계약서 ISessionService 구현 (매칭 큐 · 친선전 방)
│   ├── MatchResultReporter.cs         - 경기 끝나면 전적 자동 저장 (게임 코드 수정 없이 붙임)
│   └── WebGLBridge.cs + Plugins/WebGL/WowBridge.jslib - WebGL 빌드가 웹 UI 로그인 공유
│
└── Docs/
    ├── Backend/WebUiServer.md         - 웹 화면 목록 · Google 로그인 · Unity 연결 상세
    ├── Backend/WebDesign.md, WebProduct.md - 웹 UI 디자인 규칙 · 제품 설명
    └── Input/MediaPipeHandInput.md    - 손 추적기 메시지 형식 · 기준값 조정
```

---

## 2. 실행

1. `Backend/run_server.bat` 더블 클릭 → 브라우저에서 `http://localhost:8080/`
   - 데모 계정 `demo@wow.dev` / `demo1234`, 관리자 `admin@wow.dev` / `admin1234`
   - DB(`Backend/wow_database.sqlite`)는 첫 실행 때 자동 생성 + 데모 데이터 시딩 (git 에는 안 올라감)
2. Quest 실기: 서버 PC 에서 `open-firewall.bat` 을 관리자 권한으로 한 번 실행 →
   Unity `BackendClient` 의 Server Url 을 `http://<서버 PC IP>:8080` 으로.

---

## 3. Unity 에서 쓰기

모든 서비스는 static 이라 씬에 붙일 필요 없이 바로 부릅니다. 콜백은 메인 스레드에서 옵니다.

```csharp
using SpellboundVR.Backend;

AuthService.Login("demo@wow.dev", "demo1234",
    user => Debug.Log($"{user.nickname} 로그인"), err => Debug.LogWarning(err));

DeckService.GetMine(codes => deckData.CopyFrom(DeckService.ToSpellIds(codes)));   // "S03" ↔ 3, "H02" ↔ 102
DeckService.Save(DeckService.ToCodes(deckData.ToArray()), _ => {}, err => ShowError(err)); // 8장 · 중복 · 고위력 ≤ 2 검사

ISessionService session = BackendSessionService.Instance;
BackendSessionService.Instance.OnMatchFound += s => StartMatch(s.opponentNickname, s.bot);
session.RequestMatchmaking();   // 15초(서버 설정) 안에 사람이 없으면 봇
```

| 화면 (§23.1) | 서비스 |
| --- | --- |
| C01 로그인 | `AuthService` (가입 · 로그인 · 게스트 · 자동 로그인 · 로그아웃) |
| C03 마이페이지 | `ProfileService`, `MatchResultService.GetHistory` |
| C05 친구 | `FriendService`, `PresenceService.GetOnline` |
| C06 친선전 | `BackendSessionService` (CreateFriendlyRoom · Join · FillWithBot · SetReady · StartFriendlyMatch) |
| C07 우편 · 공지 | `MailService` (공지 포함 목록 · 읽음 · 삭제 · 초대), `NoticeService` |
| C08 랭킹 | `RankingService` |
| C09 마법서 | `DeckService`, `SkillService` |
| C10 매칭 대기 | `BackendSessionService.OnMatchmakingProgress` (경과 시간 · 대기 인원) |
| C13 결과 | `MatchResultReporter.OnResultSaved` (레이팅 변화 문구) |

**전적 자동 저장**: 게임 씬에 빈 GameObject 를 만들고 `MatchResultReporter` 를 붙이면 끝입니다.
`GameBootstrap` 을 자동으로 찾아 `OnMatchEnded` 를 구독하고, 매칭 경기는 레이팅에 반영(±16),
봇 연습 · 친선전은 기록만 합니다(§22.2). 로그인 안 했거나 전용 서버 빌드면 아무것도 안 합니다.

**아직 연결 안 된 것**: 매칭 · 친선전이 성사된 뒤 실제 Photon Fusion 세션을 시작하는 부분은
`BackendSessionService` 이벤트(`OnMatchFound`, `OnFriendlyMatchStarted`)를 받아 Network/Session 쪽에서 해야 합니다.

---

## 4. 팀원 원본에서 바꾼 점

| 바꾼 것 | 이유 |
| --- | --- |
| `AI/MediaPipe` → `GestureTools/MediaPipe` | 기획서 §28 폴더 규칙 (제스처 도구는 GestureTools) |
| Unity `API/` · `WarOfWizardsBackend.cs` 를 새로 작성 | 예전 Spring 서버 기준이라 Python 서버와 경로가 여러 곳 달랐고(친구 수락 · 우편 읽음 · 부트스트랩 형식 등), `UserDataLoader.cs` 는 없는 클래스를 참조해 컴파일이 깨졌음 |
| 서버 `POST /api/rooms` 가 `{ "code": "ABC123" }` 를 받음 | `ISessionService.CreateFriendlyRoom()` 은 코드를 바로 돌려줘야 해서. 이미 쓰이는 코드면 서버가 새로 정함 |
| 가입 환영 우편의 "골드 500" 보상 제거 | §23.1 "우편함에는 보상 · 아이템이 없다" |
| S12 이름 "마법사" → "드래곤" | 기획서 §14.3 · 게임 SpellDefinition 과 맞춤 |
| `.gitignore` · `.gitattributes` 추가 | DB · MediaPipe 모델 자동 생성 파일 제외, `.bat` 은 CRLF |

**가져오지 않은 것** (필요하면 원격 `feat/web-ai-backend` 브랜치에 그대로 있음)

- `Backend/SpringServer/`, `Database/oracle/` — Python 서버가 같은 기능을 대신함
- `stage_server.py` · `stages.html` — 8단계 시험용 별도 서버 (wow_server.py 와 중복)
- `static/legacy/` — 예전 화면 (옛 주소는 새 화면으로 자동 이동)
- `AI/Tools` · `AI/Training` · `AI/Models` — Spring 전용 API 기반 LSTM 학습 도구. 게임은 온디바이스 $P+ 를 씀(§9)
- Unity `Contracts/` · `Gesture/` — 이 저장소 쪽이 더 최신

---

## 5. 확인한 것

- `python Backend/test_web_ui_api.py` → 59개 모두 통과 (방 코드 제안 검사 2개 추가)
- Unity 와 같은 순서의 흐름 확인: 로그인 → 덱 → 매칭(봇) → 게임 방 참가 → 승리 보고 → 레이팅 1200 → 1216
- Unity C# (`_Project/Backend/*.cs`) 을 Unity 6 DLL + 현재 `Assembly-CSharp.dll` 로 컴파일 → 오류 0
- `GestureTools/MediaPipe/test_hand_pipeline.py` 는 11개 중 4개 실패 — 팀원 원본에서도 똑같이 실패함
  (2개는 전역 Python 에 `cv2` 가 없어서, 2개는 원래 있던 문제). 손 추적기 자체는 `.venv` 로 실행하면 됨.

Unity 에디터를 처음 열면 새 파일들의 `.meta` 가 생성됩니다. 그것까지 함께 커밋하세요.
