# War of Wizards (Spellbound VR)

Quest 3 핸드트래킹으로 룬을 그려 마법을 쓰는 1:1 VR 대전 게임. 3개 레인 위에서 미니언 · 소환수가 싸우고, 플레이어는 룬 19종(일반 13 · 고위력 6) 중 8장으로 덱을 짜서 상대 타워 → 넥서스 → 플레이어 순으로 무너뜨린다.

## 폴더

| 폴더 | 내용 |
| --- | --- |
| `UnityClient/` | Unity 6 (6000.3.10f1) 프로젝트. 게임 코드는 `Assets/_Project/`, 씬은 `Assets/Scenes/Client/04_Battle.unity` 하나 |
| `Backend/` | Python 웹 서버 (`wow_server.py`) · 웹 UI (`static/`) — 로그인 · 마법서(덱) · 랭킹 · 매칭 |
| `GestureTools/MediaPipe/` | 웹캠 손 추적 → Unity 입력 (MediaPipe) |
| `Docs/` | 분야별 문서 (아래) |
| `Builds/` | 빌드 출력 (저장소에는 `.keep`만) |

## 문서

| 문서 | 내용 |
| --- | --- |
| [`Spellbound_VR_Master_Prompt_v6.0.md`](Spellbound_VR_Master_Prompt_v6.0.md) | 게임 규칙 · 스킬 19종 수치표(§14.3) · 서버 권한 구조 — **기준 문서** |
| [`Docs/GameDesign/VrTestGuide.md`](Docs/GameDesign/VrTestGuide.md) | **Quest로 처음부터 끝까지 테스트** — 빌드(★ VR 전체 흐름 APK) · 설치 · 헤드셋 조작 · 체크리스트 · 문제 해결 |
| [`Docs/GameDesign/GameFlow.md`](Docs/GameDesign/GameFlow.md) | 게임 전체 진행 방식 — 로그인 → 로비 → 대전 모드 → 전투 3단계 → 결과, 한 판 예시 |
| [`MANUAL_TODO.md`](MANUAL_TODO.md) | 사람이 직접 해야 할 일, 에디터에서 바로 해보기, 확인 체크리스트 |
| [`Spellbound_VR_Weekly_Roadmap_v3.0.md`](Spellbound_VR_Weekly_Roadmap_v3.0.md) | 8주 주차별 계획 |
| [`BACKEND_INTEGRATION.md`](BACKEND_INTEGRATION.md) | 백엔드 · 웹 UI 통합 안내 |
| [`Docs/Art/AssetIntegrationGuide.md`](Docs/Art/AssetIntegrationGuide.md) | 룬 · 스킬 VFX · 모델 에셋 규칙, 에셋 빌더, 에셋 정리 규칙 |
| [`Docs/Art/Licenses.md`](Docs/Art/Licenses.md) | 외부 에셋 팩 출처 · 사용처 |
| [`Docs/Backend/`](Docs/Backend/) | 웹 디자인 · 제품 · 서버 API |
| [`Docs/Backend/MultiplayerAi.md`](Docs/Backend/MultiplayerAi.md) | 개인전 2~4명 매칭 · 공식 팀 계정(1조~4조) · AI 전략 코치(VLM) · 서버 MediaPipe |
| [`Docs/Backend/AiLearning.md`](Docs/Backend/AiLearning.md) | AI 가 게임을 배우게 하기 — 경험 수집 · 판 결과로 자동 학습 · VLM 미세조정 |
| [`Docs/Bot/ManualControlKeys.md`](Docs/Bot/ManualControlKeys.md) | 키보드 조작 · 봇 수동 조종 키 |
| [`Docs/Bot/AiStrategyBot.md`](Docs/Bot/AiStrategyBot.md) | AI 전략 봇 (랜덤 봇 대신, 같은 시전 속도에서 승률 90%) · 서버 학습 연결 · 승률 측정기 BotLab |
| [`Docs/Input/MediaPipeHandInput.md`](Docs/Input/MediaPipeHandInput.md) | MediaPipe 손동작 입력 · **19종 룬 안내표** (그림: `GestureTools/MediaPipe/rune_sheet.png`) |

## 빠르게 실행

1. Unity Hub에서 `UnityClient/`를 열고 `04_Battle` 씬에서 Play — 로컬 봇전이 시작된다 (헤드셋 없이 키보드 · 마우스로 조작).
2. 웹 서버: `Backend/run_server.bat`.

자세한 순서는 `MANUAL_TODO.md` 1장.
