# War of Wizards — 8단계 순차 로드맵 전용 파이썬 서버 개발 가이드

본 문서는 **한꺼번에 모든 복잡한 기능을 구현하지 않고**, 단계별로 점진적 개발 및 검증을 수행할 수 있도록 설계된 **「8단계 로드맵 전용 파이썬 서버 (`stage_server.py`)」**의 명세서 및 활용 안내서입니다.

---

## 🎯 8단계 개발 로드맵 개요

| 단계 | 개발 영역 | 주요 목표 및 백엔드 연동 범위 | 검증 엔드포인트 |
|:---:|:---|:---|:---|
| **1단계** | Unity 기본 전투 | 3D 맵 캐릭터 이동 (좌/중/우) & 키보드(1~8번) 마법 발사 로직 | `GET /api/stage1/combat-config` |
| **2단계** | Photon 방 연결 | 2명 플레이어 동시 접속 및 캐릭터 움직임 네트워크 동기화 검증 | `POST /api/stage2/photon/auth`<br>`GET /api/stage2/photon/rooms` |
| **3단계** | 백엔드 연동 | 회원가입, 로그인(JWT), 방 생성 및 6자리 방 코드 입장 API | `POST /api/stage3/auth/register`<br>`POST /api/stage3/auth/login`<br>`POST /api/stage3/rooms/create`<br>`POST /api/stage3/rooms/join` |
| **4단계** | 대기실과 매칭 | 대기실 참가자 목록, 준비 완료 토글, 매칭 취소, 퇴장 처리 | `GET /api/stage4/rooms/{code}/participants`<br>`POST /api/stage4/rooms/ready`<br>`POST /api/stage4/matchmaking/cancel`<br>`POST /api/stage4/rooms/leave` |
| **5단계** | 마법 이벤트 동기화 | 한쪽 플레이어의 마법 시전이 상대방 화면에도 실시간으로 릴레이 | `POST /api/stage5/spells/cast`<br>`GET /api/stage5/spells/stream` (SSE) |
| **6단계** | AI 손동작 인식 | 키보드 발동 대신 웹캠 MediaPipe 손 추적 + 룬 궤적($P) 분석으로 19종 마법 인식 | `POST /api/stage6/ai/recognize`<br>UDP 5065 (`AI/MediaPipe`) |
| **7단계** | 전투 판정과 전적 저장 | 체력 0 승패 판정, 랭킹 점수(MMR +20 / -15), 백엔드 DB 영구 기록 | `POST /api/stage7/combat/record-match`<br>`GET /api/stage7/rankings` |
| **8단계** | 보안 및 배포 | Photon 인증 서명 검증, 쿨타임 미경과 치트 차단, 세션 복구 | `POST /api/stage8/security/validate`<br>`POST /api/stage8/session/recover` |

---

## 🚀 서버 실행 및 단계별 테스트 방법

### 1. 서버 시작 (원클릭)
- `WAR-OF-WIZARDS/Backend/run_stage_server.bat` 더블 클릭
- 또는 터미널: `python stage_server.py 8080`

### 2. 브라우저에서 인터랙티브 8단계 비주얼 테스트
- 주소: `http://localhost:8080/stages.html`
- 웹 브라우저 화면에서 1단계부터 8단계까지의 버튼을 누르며 단계별 동작을 시각적으로 확인할 수 있습니다.
- 특히 **6단계 캔버스**에서는 마우스로 룬 문양을 직접 그리면 AI가 실시간으로 스킬 코드(S01~S13, H01~H06)와 일치율을 계산해 줍니다.

### 3. 터미널 자동 전수 테스트
- 터미널: `python test_stages.py`
- 1단계부터 8단계까지 모든 엔드포인트를 순차 호출하여 100% 정상 작동 여부를 자동 보고합니다.

---

## 💻 Unity 클라이언트 단계별 구현 팁

### 1단계: 키보드 입력 구현 (`IPlayerInput`)
- 유니티 에디터에서 숫자키 `1`~`8`번을 누르면 해당 슬롯의 스킬이 발사되도록 구성합니다.
- 좌/우 이동은 `LeftArrow`/`RightArrow` 또는 `A`/`D` 키로 3개 레인(`Left`, `Center`, `Right`)을 이동합니다.

### 2단계: Photon 연결 (`PhotonConnector`)
- 백엔드 `POST /api/stage2/photon/auth`로 토큰을 받아 Photon Cloud에 접속합니다.
- `PhotonNetwork.JoinOrCreateRoom()`으로 2인을 매칭하고 `PhotonTransformView`로 위치를 동기화합니다.

### 3단계 & 4단계: 방 생성 및 대기실 UI
- `WarOfWizardsBackend.Instance`의 `CreateFriendlyRoom()` 및 `JoinFriendlyRoom(code)`를 호출합니다.
- 대기실에서 두 플레이어가 모두 준비(`SetReady(true)`)하면 인게임 전투 씬으로 전환합니다.

### 5단계: 마법 시전 동기화
- 플레이어가 마법을 쏠 때 `POST /api/stage5/spells/cast`로 좌표 및 스킬 ID를 전송합니다.
- 상대방 클라이언트는 실시간 이벤트를 수신하여 해당 열(Column)에 마법 투사체 VFX를 생성합니다.

### 6단계: AI 제스처인식 연결
- **웹캠 + MediaPipe**: `AI/MediaPipe/run_hand_tracker.bat` 를 켜면 손동작과 룬 인식 결과가 UDP 5065 로 Unity 에 간다.
  Unity 는 `AI/MediaPipe/UnityReceiver/MediaPipeHandInput.cs`(`IPlayerInput` 구현)로 받는다. 자세한 내용: `Docs/MEDIAPIPE_HAND_INPUT.md`
- 서버 인식: 궤적 포인트 목록을 `POST /api/stage6/ai/recognize`로 전송합니다 (`candidates`: 쿨타임 끝난 룬만, `yDown`: 웹 캔버스 좌표).
  서버와 MediaPipe 추적기는 같은 19종 룬 인식기(`AI/MediaPipe/wow_runes.py`)를 쓴다.
- 서버가 반환한 `spellId`(예: `S03`)를 받아 키보드 입력 대신 해당 스킬을 자동 장착 및 준비 상태로 전환합니다.

### 7단계: 경기 종료 및 승패 기록
- 플레이어 체력이 0이 되었을 때 `POST /api/stage7/combat/record-match`를 호출하여 점수와 전적을 DB에 저장합니다.

### 8단계: 서버 권한 보안 검증
- 클라이언트가 임의로 쿨타임을 무시하고 패킷을 전송하지 못하도록 서버의 `POST /api/stage8/security/validate`를 거쳐 인가된 스킬만 발생하도록 검증합니다.
