# 구현 현황 — War of Wizards

2026-11-02 기준으로 **지금 저장소에 실제로 들어 있는 것**을 정리한 문서다. 무엇이 됐고, 어디까지 확인했고, 무엇이 남았는지를 여기서 본다. 자세한 내용은 각 문서로 이어진다.

규칙의 출발점은 [`마스터 프롬프트`](../Spellbound_VR_Master_Prompt_v6.0.md)(1:1 기준)이고, 그 뒤에 바뀐 것은 이 문서와 [`Ffa4.md`](Battle/Ffa4.md)가 기준이다.

---

## 1. 한눈에 보기

| 영역 | 지금 상태 | 자세히 |
| --- | --- | --- |
| 경기 | 4인 개인전(기본) · 1:1. 2~4명 매칭, 빈자리는 AI. 탈락 후 관전 | [`Ffa4.md`](Battle/Ffa4.md) · [`GameFlow.md`](GameDesign/GameFlow.md) |
| 맵 | 1:1 직사각형 전장(아트 적용) · 4인 마름모 전장(실행 중 생성) | [`ArenaCoordinateSpec.md`](Input/ArenaCoordinateSpec.md) |
| 입력 | Quest 핸드트래킹 · PC 웹캠 손 모양(기본) · 마우스 · 키보드 | [`MediaPipeHandInput.md`](Input/MediaPipeHandInput.md) · [`ManualControlKeys.md`](Bot/ManualControlKeys.md) |
| AI | 봇 두뇌 + VLM 작전(하이브리드) · 판 결과 학습 · 사람용 코치 | [`AiStrategyBot.md`](Bot/AiStrategyBot.md) · [`AiLearning.md`](Backend/AiLearning.md) |
| 서버 | `StartServer.bat` 하나로 백엔드 · 4인 · 1:1 게임 서버 | 3장 |
| 로그인 | 이메일 · 게스트 · 공식 팀 계정 · Google(코드 입력 없음) | [`GameFlow.md`](GameDesign/GameFlow.md) 2장 |
| 아트 | 미니언 · 소환수 · 구조물 · VFX 19종 + 플레이어 아바타(Polytope) | [`AssetIntegrationGuide.md`](Art/AssetIntegrationGuide.md) |

---

## 2. AI 구조

"AI"는 네 가지가 층으로 쌓여 있다. 위쪽이 없어도 아래쪽만으로 게임은 돌아간다.

```text
 ① 봇 두뇌 (Unity, 항상)     0.25초마다 판단. 피하기 · 스킬 점수 매기기. 서버 · GPU 없이 돈다
        ▲ 작전(orders) · 한 수
 ② VLM (서버, 켜져 있을 때)   전장 지도 그림을 보고 3초마다 작전을 정한다. 꺼져 있으면 규칙 AI 가 답한다
        ▲ 이긴 판의 스킬 · 예시
 ③ 학습 (서버, 자동)         판 결과로 "이 상황에서 이 스킬이 이겼나"를 쌓아 ①② 에 반영
        ▲ 이긴 판 장면
 ④ VLM 미세조정 (GPU, 수동)  모은 장면으로 모델 자체를 학습 (LoRA). 진짜 판 데이터로는 아직 안 했다

 ⑤ AI 코치 (사람용)          같은 서버 API 로 HUD 에 조언만 보여 준다. 판정에는 영향 없음
```

| 층 | 무엇으로 | 코드 |
| --- | --- | --- |
| 봇 두뇌 | 가중치 점수 (`BotLab tune` 으로 찾음), 4인은 `AiBrainWeights.Ffa` | `Bot/Brains/AiStrategyBrain.cs` |
| VLM | 순서대로 Claude(API 키가 있을 때) → 로컬 Qwen2.5-VL-3B(llama.cpp) → 규칙 AI. 설정 `ai.vlm_provider = auto` | `Backend/wow_ai.py` · `wow_board.py` |
| 전장 지도 | 사진이 없으면 서버가 384×512 그림을 그려 VLM 에 준다. 4인은 위쪽에 상대 칸 | `Backend/wow_board.py` |
| 학습 | 상황 갈래별 스킬 가치, 평균과의 차이(advantage)로 비교. 이름표 20개마다 자동 학습 | `Backend/wow_learn.py` |
| 코치 | 6초마다 조언, 로그인 + 서버가 있을 때만 | `Backend/BattleCoach.cs` (Unity) |

**"VLM"은 Vision Language Model** — 그림을 읽는 언어 모델이다. 3D 아바타 형식인 VRM 과는 다르고, 이 프로젝트는 VRM 을 쓰지 않는다.

### 측정값

| 항목 | 값 | 언제 · 어디서 |
| --- | --- | --- |
| AI 전략 vs 랜덤 | 99.0% (같은 시전 속도 90.3%) | 2026-10-26, BotLab 500판 |
| 소환 쿨타임 8초 적용 뒤 | 99.3% (같은 속도 89.2%), AI 끼리 평균 426초 · 시간 초과 23% | 2026-10-29, BotLab 300판 |
| 하이브리드 봇끼리 | 10판, VLM 답 727번 평균 3.2초 · 실패 0 | 2026-10-29, RTX 4060 노트북 |
| 4인 개인전 | 200판 평균 447초 · 시간 초과 0 · 무승부 6 · 자리별 승률 고름 | 2026-11-01, BotLab |

> **PC 마다 다르다.** VLM 모델과 학습 데이터는 저장소에 없다 (`Backend/ai_data/` · DB 는 git 제외). 2026-11-02 현재 이 저장소를 받은 kiwan PC 에는 로컬 VLM 모델이 없고 학습 데이터도 0건이다. 그래서 `StartServer.bat` 은 "llama.cpp 또는 모델 파일이 없습니다"라고 알린 뒤 규칙 AI 로 켜진다. 위 VLM 수치는 모델을 설치한 다른 PC 의 측정값이다. 설치법: [`AiLearning.md`](Backend/AiLearning.md) 4-1.

---

## 3. 서버 실행

| 하고 싶은 것 | 방법 |
| --- | --- |
| 전부 켜기 | 저장소 맨 위 `StartServer.bat` 더블클릭 — 백엔드 + 로컬 VLM(있으면) + 4인 · 1:1 게임 서버(창 없음). 게임 서버가 꺼지면 다시 켠다. 창을 닫으면 모두 꺼진다 |
| PC 에서 게임만 켜기 | 그냥 Play — 서버가 꺼져 있으면 로그인 화면이 `StartServer.bat` 을 실행한다 |
| PC 켤 때 자동으로 | `ServerAutostart.bat` 한 번 실행 (다시 실행하면 해제) |
| 서버 창을 보며 시험 | `Builds/Server/RunServer.bat`(4인) · `RunServer1v1.bat`(1:1) |

서버 빌드 메뉴를 쓰면 돌던 게임 서버가 꺼졌다가 빌드가 끝나면 다시 켜진다. 서버 포트: HTTP 8080, 손 추적 UDP 5065 · 5066 · 5068, 로컬 VLM 8790.

---

## 4. 무엇을 어디까지 확인했나

### 실제로 돌려서 확인

| 항목 | 방법 | 결과 |
| --- | --- | --- |
| 백엔드 API | `python Backend/test_web_ui_api.py` | 124개 통과 |
| 4인 규칙 | `dotnet run -c Release -- ffatest` (Tools/BotLab) | 통과 (라인별 피해 · 넥서스 잠금 · 관전 포함) |
| 런타임 스크립트 | 실제 Unity · Fusion · Meta DLL 로 컴파일 | 오류 0 |
| 서버 빌드 | Unity 빌드 메뉴 (2026-11-02) | 성공 (Meta SDK 후처리 예외는 결과에 영향 없음) |
| 1:1 서버 분리 | `-duel -batchmode -nographics` 로 실행 | `Server running` — 이전 빌드는 `-duel` 이 무시되어 꺼졌다 |
| 손 모양 인식 | MediaPipe 공식 손 사진으로 만든 영상 → 추적기 → UDP | 인식 · 스킬 선택 · 미리보기 · 종료까지 동작 |
| 손 입력 시험 | `test_hand_pipeline.py` | 19개 중 16개 통과. 실패 3개는 룬 모양을 바꾸기 전 값을 기대하는 낡은 시험 |
| QR 생성기 | 디코더로 읽기 | 98/98, Google 페이지에 코드가 채워지는 것도 확인 |
| 게임 서버 감시 | 가짜 실행 파일로 시험 | 인자 전달 · 빌드 중 대기 · 부모가 꺼지면 같이 꺼짐 |

### 아직 눈으로 확인하지 못한 것

| 항목 | 이유 · 확인 방법 |
| --- | --- |
| 웹캠으로 실제 손 플레이, 오른쪽 아래 패널 배치 | 실제 사람 손 · Unity Play 기록이 없다. `04_Battle` Play 후 봇 연습전 |
| 아바타 외형, 지팡이 쥔 모습 | 프리팹 가져오기까지만 확인. 머티리얼을 Built-in `Standard` 로 고친 뒤 아직 Unity 로 보지 않았다. 상대가 보이는 경기에서 확인 |
| 새 Quest APK | 2026-11-02 12:22 빌드(`Spellbound.apk`)는 아바타 머티리얼을 고치기 전에 시작해 약 2시간 40분 걸렸고 아바타가 분홍색으로 나온다. 다시 빌드해야 한다 ([`AssetIntegrationGuide.md`](Art/AssetIntegrationGuide.md) 0장) |
| 4인전 시야 · 구조물 크기 체감 | 수치는 계산으로 정했다. `ffaViewLift` · `ffaModelScale` 로 조정 |
| 관전 화면 · 메뉴 · 컨트롤러 전환 | 규칙 검사만 통과. 4인전에서 탈락해 보기 |
| Google 로그인 전체 과정, Quest QR 스캔 | 코드 채움까지만 확인. 실제 계정으로 한 번 |
| 사람끼리 온라인 4인전 (Quest 여러 대) | 장비가 필요하다. [`VrTestGuide.md`](GameDesign/VrTestGuide.md) 6장 |

---

## 5. 남은 일 · 알려진 문제

| 항목 | 내용 |
| --- | --- |
| 씬 실행 모드 | `04_Battle` 의 `GameBootstrap.bootMode` 는 `Auto` 가 기본인데, 작업 폴더에는 `FusionClient`(3), 마지막 커밋에는 `DedicatedServer`(2) 로 들어 있다. 빌드 메뉴가 빌드하는 동안 이 값을 바꾸므로 빌드 중에 커밋하지 않는다. 메뉴 `Spellbound > Mode > Auto` 로 맞춘 뒤 커밋한다 |
| 튜토리얼 | 아직 룬 그리기(VR) 기준이다. 웹캠 손 모양은 전투 패널의 안내로만 알려 준다 |
| VLM 학습 | 진짜 판 데이터로 LoRA 를 학습하지 않았다. 학습한 모델을 실시간으로 쓰려면 GGUF 변환이 필요하다 |
| 화면 사진 | Unity 봇은 화면 사진을 보내지 않고 서버가 그린 전장 지도를 쓴다. 미세조정 데이터를 늘리려면 Unity 쪽 캡처가 필요하다 |
| 밸런스 | 소환 쿨타임을 8초로 바꾼 뒤 가중치를 다시 찾지 않았다. 1:1 AI 끼리 시간 초과가 23% 까지 늘었다 |
| 외부 에셋 | 출처 · 라이선스 "확인 필요" 항목이 남아 있다 ([`Licenses.md`](Art/Licenses.md)). `VFX` 팩 미사용분은 게임이 이름으로 찾는 파일이 섞여 있어 아직 정리하지 않았다 |
| 낡은 시험 | `test_hand_pipeline.py` 의 룬 시험 3개를 현재 룬 모양에 맞게 고쳐야 한다 |
