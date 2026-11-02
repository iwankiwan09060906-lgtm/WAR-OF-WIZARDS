# War of Wizards — MediaPipe 손동작 입력 (6단계: AI 손동작 인식)

웹캠으로 손을 찍어 **MediaPipe Hand Landmarker(양손 21점)** 로 손 모양을 인식하고,
결과를 **UDP(JSON)** 로 Unity 클라이언트에 보낸다. 헤드셋 없는 PC 의 기본 입력이다.

**쓰는 법: 그냥 게임을 켠다.** 전투가 시작되면 게임이 추적기를 창 없이 직접 띄우고(`WebcamTracker.cs`), 화면 오른쪽 아래에
내 모습과 내 덱의 룬 그림이 나온다(`WebcamHandPanel.cs`). 전투가 끝나면 추적기도 꺼진다. 환경설정 > 제스처 > "웹캠 손 입력"을 끄면 마우스 · 키보드로 돌아간다.

## 0. 룬 그리기 입력 (기본)

| 손 | 동작 | 결과 |
| --- | --- | --- |
| 오른손 | 엄지 · 검지를 붙인 채 허공에 룬을 그리고 뗀다 | 한 획. 여러 획이면 1.2초 안에 다시 집어 이어 그린다 |
| 오른손 | 다 그리면 그대로 1.2초 기다린다 (주먹을 쥐면 바로) | 룬을 인식해 스킬을 고른다. 자기 · 타워 · 전역 스킬은 바로 시전 |
| 오른손 | 인식된 뒤 손을 움직인다 | 검지 끝 위치가 조준점 — 3초 뒤 시전 |
| 오른손 | 손바닥을 편다 | 시전 취소 |
| 왼손 | 손바닥 편 채 좌/우로 휘두른다 | 한 칸 이동 |

화면 오른쪽 아래에 내 덱의 룬 그림이 나온다. 궤적은 화면 비율 좌표라 추적기가 `pinch_end` 에 `aspect` 를 같이 보내고, Unity 가 x 에 곱해 16:9 화면에서 찌그러지지 않은 모양으로 인식한다.

## 0-1. 손 모양 입력 (환경설정 "웹캠으로 룬 그리기"를 끄면)

| 손 | 동작 | 결과 |
| --- | --- | --- |
| 오른손 | 아래 표의 손 모양을 약 0.5초 유지 (환경설정 "유지 시간") | 그 스킬을 고른다. 자기 · 타워 · 전역 스킬은 바로 시전 |
| 오른손 | 고른 뒤 손을 움직인다 | 손바닥 위치가 조준점 — 3초 뒤 시전 (이때 손 모양을 바꿔도 스킬은 그대로) |
| 오른손 | 화면 밖으로 뺀다 | 시전 취소 |
| 왼손 | 손바닥 편 채 좌/우로 휘두른다 | 한 칸 이동 |

왼손잡이는 환경설정 > 조작 > "왼손잡이 모드"를 켜면 위 표의 오른손 · 왼손이 맞바뀐다 (이미 켜진 추적기에도 2초 안에 반영된다, 헤드셋은 다음 경기부터).

키보드도 같이 된다: `A` · `D` 이동, `1`~`8` 슬롯. 추적기가 없거나 웹캠을 열 수 없으면 마우스가 조준을 맡는다.

패턴은 (엄지, 검지, 중지, 약지, 새끼) 순서, 1 = 폄, 0 = 접음. 표는 `hand_gestures.SKILL_SHAPES` 한 곳에만 있고 게임 안내판도 이 표를 받아 쓴다.

| 패턴 | 스킬 | 패턴 | 스킬 |
| --- | --- | --- | --- |
| `00000` 주먹 | S01 연막 | `00111` | S11 도끼 전사 |
| `01000` | S02 독 장판 | `00110` | S12 드래곤 |
| `01110` | S03 파이어볼 | `11110` | S13 터렛 |
| `01111` | S04 화살 세례 | `01100` 브이 | H01 프리즈 |
| `11111` 손바닥 | S05 토네이도 | `10001` | H02 레이저 빔 |
| `10000` 엄지 척 | S06 번개 | `11000` | H03 흡혈 |
| `11100` | S07 총난사 | `01001` | H04 반사 결계 |
| `11001` | S08 방어 쉴드 | `00001` | H05 분노 |
| `00100` | S09 타워 버프 | `00011` | H06 소환탑 |
| `10100` | S10 집중 | | |

- 잘 잡히려면: 손바닥이 카메라를 향하게, 카메라에서 30~50cm, 밝은 곳에서.
- 표에 없는 모양과 손을 바꾸는 도중의 모양은 고르지 않는다. 같은 모양을 계속 들고 있어도 한 번만 고른다.
- 덱에 없는 스킬은 "이 스킬은 덱에 없습니다"가 뜬다 (스킬 테스트 모드에서는 시전된다).

---

아래는 추적기 내부와 룬 그리기 방식이다.

```text
 웹캠 ──▶ wow_hand_tracker.py ──UDP 5065──▶ Unity: MediaPipeHandInput : IPlayerInput
          (MediaPipe 21점 →               ◀──UDP 5066── (쿨타임 끝난 룬 후보 목록)
           손 자세 · 룬 인식)                              │
              │ (선택) --recognizer server                  ▼
              └──HTTP──▶ 백엔드 POST /api/stage6/ai/recognize   시전 상태 머신(§6.2) → SpellCastRequest
```

| 파일 | 역할 |
|---|---|
| `GestureTools/MediaPipe/wow_hand_tracker.py` | 웹캠 → MediaPipe → 손 이벤트 · 룬 인식 → UDP 송출, 미리보기 창 |
| `GestureTools/MediaPipe/hand_gestures.py` | 21점 → 핀치/주먹/가리키기/손바닥, 왼손 휘두르기 판정 (카메라 없이 시험 가능) |
| `GestureTools/MediaPipe/wow_runes.py` | 19종 룬 인식기 ($P Point-Cloud). 백엔드 `Backend/wow_server.py` 의 `/api/runes/recognize` 도 같은 파일을 쓴다 |
| `GestureTools/MediaPipe/udp_monitor.py` | Unity 없이 출력 확인용 "가짜 Unity" 수신기 |
| `Assets/_Project/Input/MediaPipe/MediaPipeHandInput.cs` | Unity 수신 · 입력 (`IPlayerInput` 구현) |
| `Assets/_Project/Input/MediaPipe/WebcamTracker.cs` | 추적기 프로세스를 창 없이 켜고 끈다 |
| `Assets/_Project/UI/C12_HUD/WebcamHandPanel.cs` | 화면 오른쪽 아래 내 모습 · 손 모양 안내 |
| `GestureTools/MediaPipe/rune_sheet.png` | 19종 룬 안내 그림 (초록 점 = 시작점) |
| `GestureTools/MediaPipe/test_hand_pipeline.py` | 자동 검사 24개 (카메라 · Unity 없이) |

---

## 1. 실행

```bat
GestureTools\MediaPipe\run_hand_tracker.bat
```

게임이 알아서 켜므로 직접 실행할 일은 없다. 미리보기 창으로 손 인식을 따로 점검할 때만 쓴다.

- 처음 실행하면 `GestureTools/MediaPipe/.venv` 가상환경을 만들고 MediaPipe 를 설치한다 (1~3분, 인터넷 필요). 게임이 켤 때는 화면 오른쪽 아래에 설치 중이라고 나온다.
  **전역 파이썬에 직접 설치하지 않는다** — MediaPipe 가 numpy 를 2.x 로 올려 다른 도구(aider, scipy 등)와 충돌한다.
- 손 모델 `models/hand_landmarker.task`(구글 공식, 7.8MB)가 없으면 처음 실행할 때 자동으로 받는다.
- 미리보기 창 단축키: `q` 종료 · `h` 왼손/오른손 바꾸기 · `a` 조준 방식(위치/기울기) · `r` 룬 안내 켜기/끄기

Unity 없이 확인하려면 다른 창에서 `run_unity_simulator.bat` 를 켜면 받은 이벤트가 `IPlayerInput` 이름으로 출력된다.

| 옵션 | 뜻 |
|---|---|
| `--camera 1` | 다른 카메라 사용 |
| `--input rune` | 손 모양 대신 핀치로 룬 그리기 (직접 실행 기본 `shape`, 게임은 환경설정을 따른다) |
| `--hold-frames 15` | 손 모양을 유지할 프레임 수 |
| `--preview-port 5068` | Unity 로 내 모습(JPEG)을 보낼 UDP 포트, 0 = 끔 |
| `--exit-idle 30` | Unity 메시지가 이 시간(초) 없으면 종료 (게임이 켤 때 쓴다) |
| `--deck S03,S06,S07,S08,S11,S13,H01,H02` | 룬 후보 고정 (Unity 가 후보를 보내면 그것으로 바뀜) |
| `--host 192.168.0.10` | Unity 가 다른 PC 에 있을 때 (그 PC 방화벽에서 UDP 5065 허용) |
| `--recognizer server --server http://localhost:8080` | 룬 인식을 백엔드 6단계 API 로 (서버가 꺼져 있으면 자동으로 로컬 인식) |
| `--swap-hands` | 왼손잡이: 왼손으로 그리고 오른손으로 이동 (게임에서는 환경설정 "왼손잡이 모드"가 대신한다) |
| `--aim angle` | 조준을 검지 끝 위치 대신 검지 기울기로 |
| `--video 영상.mp4` | 카메라 대신 영상 파일 (시험용, 영상 속 시간으로 판정) |
| `--no-preview` · `--no-landmarks` | 창 없이 실행 · frame 메시지에서 21점 빼기 |

---

## 2. 손동작 → 게임 입력 (마스터 프롬프트 §6)

화면은 거울처럼 좌우가 뒤집혀 보인다. 손 판정도 거울 화면 기준이라, 사용자가 오른쪽으로 움직이면 화면에서도 오른쪽이다.
MediaPipe 의 왼손 · 오른손 라벨은 넣어 준 그림 속 손 모양 그대로라서 뒤집은 화면에서는 반대로 나온다 — `split_hands` 가 라벨을 맞바꾼다.

| 손 | 동작 | UDP 이벤트 | IPlayerInput | 상태 머신 (§6.2) |
|---|---|---|---|---|
| 오른손 | 엄지·검지 끝을 붙인다 (핀치) | `pinch_start` | `OnPinchStarted` | Idle → Drawing |
| 오른손 | 붙인 채 룬을 그리고 뗀다 | `pinch_end` (+궤적, +룬 결과) | `OnPinchEnded(Vector2[])` | Recognizing → RuneReady |
| 오른손 | 주먹 | `fist` | `OnFistDetected` | RuneReady → Armed (자기/전역 스킬은 즉시 시전) |
| 오른손 | 검지만 펴서 가리킨다 | `point` (+열 0/1/2, 깊이 0~1) | `OnPointDetected` | Armed → Aiming (3초 뒤 시전, 게임의 웹캠 룬 그리기는 이 단계 없이 바로 조준) |
| 오른손 | 손바닥을 편다 | `palm_open` | `OnPalmOpenDetected` | 취소 (쿨타임 미소모) |
| 왼손 | 손바닥 편 채 왼쪽/오른쪽으로 휘두른다 | `move` (-1/+1) | `OnMoveGesture(sbyte)` | 한 칸 이동 |
| 양손 | 화면에 들어옴/나감 | `tracking` | `IsRight/LeftHandTracked` | 끊기면 즉시 취소 (§6.4) |

- **조준 열**: 가리키는 검지 끝이 화면 왼쪽 1/3 = L, 가운데 = C, 오른쪽 = R (미리보기 창에 3칸이 표시된다). 깊이는 손이 화면 위쪽일수록 멀다.
- **핀치를 떼면서 손을 펴는 실수**는 취소로 보지 않는다 (뗀 뒤 0.6초 무시 + 손바닥은 8프레임 이상 유지해야 인정).
- 그리는 도중 손이 화면 밖으로 나가면 `pinch_cancel` + `tracking(false)` 가 간다.

---

## 3. 19종 룬

![룬 안내](../../GestureTools/MediaPipe/rune_sheet.png)

| 코드 | 스킬 | 룬 | 코드 | 스킬 | 룬 |
|---|---|---|---|---|---|
| S01 | 연막 | 눈 | S11 | 도끼 전사 | X |
| S02 | 독 장판 | 삼각형 + X | S12 | 드래곤 | 육각별 |
| S03 | 파이어볼 | 왕관 | S13 | 터렛 | I |
| S04 | 화살 세례 | 세 갈래 화살 | H01 | 프리즈 | 원 안 모래시계 |
| S05 | 토네이도 | 소용돌이 | H02 | 레이저 빔 | 별표 * |
| S06 | 번개 | 번개 | H03 | 흡혈 | 심장 박동 하트 |
| S07 | 총난사 | 가로줄 5개 | H04 | 반사 결계 | 원 + 위 화살표 |
| S08 | 방어 쉴드 | 방패 | H05 | 분노 | 화난 얼굴 |
| S09 | 타워 버프 | # | H06 | 소환탑 | 집 |
| S10 | 집중 | 위쪽 화살표 | | | |

- 이름 · 룬 모양은 `Spellbound_VR_Master_Prompt_v6.0.md` §14.3 과 `Backend/static/assets/skills/rune_line/*.png` 기준이다 (이 표는 `wow_runes.SPELLS` 에서 만들었다). Unity 의 룬 모양은 `Assets/Art/Runes/Designs/*.png` · `RuneDesigns.cs` 와 같다.
- 인식 방식: $P Point-Cloud. 획 방향 · 시작점과 상관없고, **회전은 구별한다** (U 와 ∩, W 와 화살표가 다른 룬).
  사람마다 기울이거나 찌그러뜨려 그리므로 룬마다 변형본과 비교한다. (Unity 클라이언트는 별도 인식기를 쓴다 — 마스터 프롬프트 §9.1)
- 판정 기준: 점수 ≥ 0.76, 1·2위 점수 차 ≥ 0.03, 궤적 길이 ≥ 화면 폭의 6%. 기준 미달이면 `recognized:false` 와 이유를 보낸다.
- 후보 줄이기(§9): Unity 가 쿨타임 끝난 슬롯의 룬만 보내면 그 안에서만 비교한다 → 오인식이 크게 준다.
- 자체 검사 (`python wow_runes.py`): 흔들고 기울인 가짜 손그림 760개(19종 × 40), 19종 전체 비교에서
  정답 98.8% · 오인식 0% · 거절 1.2%. 아무렇게나 그은 선 200개 중 룬으로 오인 6개(3%).

---

## 4. UDP 메시지 형식

추적기 → Unity: `127.0.0.1:5065`, UTF-8 JSON, 데이터그램 1개 = 메시지 1개.
좌표는 모두 **0~1, x 오른쪽 +, y 위쪽 +** (Unity `Vector2` 와 같은 방향).
Unity `JsonUtility` 로 바로 읽을 수 있게 중첩 배열을 쓰지 않는다 (궤적 = `{"x","y"}` 배열, 21점 = 숫자 63개).

### frame (매 프레임, 약 30회/초)

```json
{"type":"frame","seq":812,"t":27.06,"fps":30.1,
 "right":{"tracked":true,"pose":"point","drawing":false,"cursor":[0.71,0.58],
          "column":2,"depth":0.62,"pinch":0.91,"landmarks":[0.52,0.21,0.0, "... 63개"]},
 "left":{"tracked":true,"pose":"open","palm":[0.31,0.44],"landmarks":["... 63개"]}}
```

`pose`: `pinch` · `fist` · `point` · `open` · `other` · `none`(안 보임)

### event (일이 생길 때)

```json
{"type":"event","seq":813,"t":27.10,"hand":"right","name":"pinch_end",
 "points":[{"x":0.50,"y":0.80},{"x":0.41,"y":0.62}],
 "length":0.43,"tooShort":false,
 "rune":{"spellId":"S03","spellNumber":3,"name":"파이어볼","score":0.91,"gap":0.21,
         "recognized":true,"reason":"ok","source":"local",
         "ranking":[{"spellId":"S03","score":0.91},{"spellId":"H04","score":0.70}]}}
```

| name | 추가 필드 |
|---|---|
| `shape` | `spell`(룬 코드), `pattern` — 손 모양 입력에서 스킬을 골랐다 |
| `pinch_start` | — |
| `pinch_end` | `points`, `length`, `tooShort`, `rune`, `aspect`(화면 가로/세로) |
| `pinch_cancel` | `reason` |
| `fist` · `palm_open` | — |
| `point` | `column`(0 L · 1 C · 2 R), `depth`(0~1) |
| `move` | `direction`(-1 왼쪽 · +1 오른쪽) |
| `tracking` | `tracked`(true/false) |

`rune.reason`: `ok` · `low_score`(모양이 다름) · `ambiguous`(두 룬과 비슷) · `too_short` · `no_candidates`
`rune.spellNumber` 는 계약서 `SpellCastRequest.SpellId` 값이다 (S01~S13 → 1~13, H01~H06 → 101~106).

### 그 밖의 추적기 → Unity 메시지

```json
{"type":"shapes","patterns":["00000","01000"],"spells":["S01","S02"]}
{"type":"status","state":"no_camera"}
```

손 모양 입력의 `frame.right` 에는 `"shape":{"pattern":"01110","spell":"S03","progress":0.6}` 가 붙는다. `cursor` 는 손바닥 중심이다 (화면 가장자리 15% 는 쓰지 않아 손이 화면 밖으로 나가지 않고 끝까지 닿는다).
UDP `5068` 로는 내 모습 JPEG 를 보낸다 (320px, 약 15장/초, 장당 약 6KB, 데이터그램 하나 = 한 장).

### Unity → 추적기 (`127.0.0.1:5066`)

```json
{"type":"candidates","spells":["S03","S06","H02"]}
{"type":"hands","swap":true,"input":"rune"}
{"type":"hello"}
{"type":"quit"}
```

`MediaPipeHandInput` 은 2초마다 마지막 후보(없으면 hello)와 왼손잡이 모드 · 입력 방식(`hands`)을 다시 보낸다 → 추적기 창에 "Unity 응답: 연결됨" 표시.

---

## 5. Unity 연결 구조

> 연결은 끝났다. 아래는 룬 그리기 입력이 코드에 어떻게 이어져 있는지 설명한다. 손 모양 입력은 `shape` 이벤트 → `CastStateMachine.SelectSpell` 로 슬롯 시전과 같은 길을 간다.

1. `Assets/_Project/Input/MediaPipe/MediaPipeHandInput.cs` 가 계약서 `IPlayerInput` 구현이다.
2. `GameBootstrap.CreateInput` 이 키보드 대신 이 컴포넌트를 만들어 시전 상태 머신에 넘긴다.
3. 룬 결정: `OnPinchEnded` 직전에 `OnRuneRecognized(RuneResult)` 가 오고 `LastRune` 에도 저장된다.
   - 그대로 쓰기: `LastRune.recognized` 이면 `LastRune.spellNumber` 로 RuneReady.
   - 직접 인식: `OnPinchEnded(Vector2[])` 궤적으로 클라이언트 $P+ 를 돌려도 된다 (같은 좌표계).
4. 쿨타임 · 고위력 소진 상태가 바뀔 때마다 `SendCandidates(Ready 슬롯 룬 코드들)` 호출 (`MediaPipeHandInput.SpellCode(spellId)` 로 변환).
5. 조준: `OnPointDetected` 시점과 그 뒤 1초 동안 `AimColumn` · `AimDepth` 를 읽어 `SpellCastRequest.TargetColumn/TargetDepth` 에 넣는다.
6. HUD (선택): `RightLandmarks` · `LeftLandmarks`(21점), `RightCursor`, `IsDrawing`, `IsTrackerConnected` 로 손 · 궤적 · 연결 상태를 그릴 수 있다.

> 추적기는 손 상태만 알려 줄 뿐 **시전 가능 여부 · 쿨타임 · 데미지는 판정하지 않는다** — 서버 권한 원칙(§8) 그대로.

---

## 6. 백엔드 룬 인식 API (`Backend/wow_server.py`)

`POST /api/runes/recognize` 가 `GestureTools/MediaPipe/wow_runes.py` 를 쓴다 (파일이 없으면 이 API 만 503).

```json
요청 {"strokes":[[{"x":..,"y":..}, ...], ...], "candidates":["S03","H02"], "yDown":false}
응답 {"result":{"spellId":"S03","bestSpell":"S03","secondSpell":"H04","spellNumber":3,"name":"파이어볼",
                "confidence":0.91,"scoreGap":0.21,"recognized":true,"reason":"ok","ranking":[["S03",0.91], ...]}}
```

- `strokes` 대신 한 획짜리 `points` 도 받는다. `candidates` (선택): 비교할 룬만. `yDown` (선택): 웹 캔버스처럼 y 가 아래로 커지는 좌표면 `true`.

---

## 7. 손 판정 기준값 조정

카메라 · 조명 · 손 크기에 따라 `GestureTools/MediaPipe/hand_gestures.py` 의 `Settings` 를 조정한다.
미리보기 창 둘째 줄에 오른손의 **핀치 값**과 **손가락 펴짐 값 4개**가 실시간으로 나온다.

| 값 | 기본 | 뜻 |
|---|---|---|
| `pinch_on` / `pinch_off` | 0.22 / 0.35 | 엄지끝-검지끝 거리 ÷ 손바닥 크기. 손끝을 붙이면 약 0.1, 손가락 붙여 편 손바닥은 약 0.38 |
| `extended` / `curled` | 1.15 / 1.00 | 손목-손끝 ÷ 손목-가운데마디. 편 손가락 약 1.2~1.35 |
| `deep_curl` | 0.80 | 검지가 이보다 접혀 있으면 핀치가 아니라 주먹 |
| `swipe_dist` / `swipe_speed` | 0.10 / 0.55 | 왼손 휘두르기: 화면 폭 10% 이상, 초당 55% 이상 |
| `palm_stable_frames` / `palm_grace_sec` | 8 / 0.6 | 손바닥 취소 오발동 방지 |

### 문제 해결

| 증상 | 해결 |
|---|---|
| "카메라를 열 수 없습니다" | 다른 프로그램(줌, 디스코드 등)을 끄거나 `--camera 1` |
| 왼손 · 오른손이 반대로 잡힘 | 환경설정 "왼손잡이 모드"가 켜져 있는지 확인. 미리보기 창에서는 `h` |
| 핀치가 잘 안 잡힘 / 너무 잘 잡힘 | 창에 나오는 핀치 값을 보고 `pinch_on` 조정 |
| 주먹이 핀치로 잡힘 | `deep_curl` 을 0.85~0.9 로 |
| Unity 가 아무것도 못 받음 | 추적기 창 위쪽 UDP 주소 확인, 다른 PC 면 `--host` + 방화벽 UDP 5065 허용 |
| 룬이 자꾸 "두 룬과 비슷해요" | 크게, 끝까지 그리기 · Unity 가 후보(`candidates`)를 보내면 훨씬 좋아진다 |

---

## 8. 검증 방법과 결과

### 2026-11-02 — 왼손 · 오른손 바로잡기 · 왼손잡이 모드

- 증상: 오른손이 이동, 왼손이 스킬로 잡혔다. MediaPipe 공식 손 사진(`woman_hands.jpg`)을 원본 · 뒤집은 그림으로 넣어 보니, 원본에서는 라벨이 실제 손과 같고 뒤집으면 반대로 나왔다. 추적기가 프레임을 뒤집은 뒤 라벨을 그대로 써서 생긴 문제였다.
- 고친 뒤 같은 사진으로 만든 영상을 실제 추적기 프로세스 → UDP 로 돌려 확인: 스킬 손이 실제 오른손이고, `hands` 메시지를 보내면 실제 왼손으로 바뀐다.
- `test_hand_pipeline.py` 24개 중 21개 통과. 새 시험(`HandSplitTest` · `SkillHandTest`)은 통과하고, 실패 3개는 아래 기록과 같은 낡은 시험이다.
- **아직 못 한 것**: 실제 사람 손 · 웹캠으로 확인.

### 2026-11-02 — 손 모양 입력

- 환경: Python 3.14 · MediaPipe 1.2.0 (`.venv`). 예전 안내와 달리 3.13 이상에서도 설치된다.
- `test_hand_pipeline.py` 19개 중 16개 통과. 손 모양 8개는 모두 통과하고, 실패 3개(`test_candidates_limit` · `test_full_cast_flow` · `test_messages_are_jsonutility_friendly`)는 룬 모양을 바꾸기 전 값을 기대하는 낡은 시험이다.
- MediaPipe 공식 손 사진으로 만든 영상 → 실제 추적기 → UDP: 펴진 손을 `11111` 로 읽어 S05 를 고름, 미리보기 장당 약 5.7KB, 시작까지 약 4초, `--exit-idle` · `quit` 종료 확인.
- **아직 못 한 것**: 실제 사람 손으로 19가지 모양을 구분하는 시험, Unity 안에서의 패널 배치. 엄지 판정은 손 방향에 민감할 수 있다.

### 2026-10-12 — 룬 그리기

```bat
cd AI\MediaPipe
.venv\Scripts\python.exe test_hand_pipeline.py   :: 자동 검사 11개
.venv\Scripts\python.exe wow_runes.py            :: 19종 룬 혼동 검사
```

- 당시 자동 검사 11개 통과: 손 자세 판정, 오른손 전체 흐름(핀치로 삼각형 → S03 → 주먹 → 가리키기 → 취소 → 추적 끊김),
  그리다가 손이 사라질 때 취소, 왼손 휘두르기(+1, 연타 무시, -1, 느린 움직임 무시), 룬 19종, 후보 제한,
  UDP 왕복, Unity `JsonUtility` 호환 형식.
- 룬 자체 검사: 760개 중 정답 751 · 오인식 0 · 거절 9.
- MediaPipe 공식 예제 손 사진으로 실제 인식 확인: 양손 21점 검출, 처리 시간 프레임당 약 16ms (CPU),
  사진을 옆으로 움직인 영상 → 추적기 → UDP → 가짜 Unity 수신까지 `tracking` · `palm_open` · `move(-1)` 전달, 후보 목록 역방향 전달 확인.
- 백엔드 6단계 API: 삼각형 → S03, 웹 캔버스 U(`yDown`) → S08, 후보 제한, 서버 꺼짐 시 로컬 대체 확인.
- **아직 못 한 것**: 실제 사람이 웹캠 앞에서 핀치로 룬을 그리는 시험. 기준값은 사진 · 가짜 랜드마크로 정했으므로
  처음 켤 때 7장 표를 보며 한 번 맞추는 것이 좋다.
