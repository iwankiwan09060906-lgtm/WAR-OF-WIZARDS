---
name: War of Wizards
description: 손으로 룬을 그려 마법을 쏘는 VR 마법사 1:1 대전의 웹 게임 클라이언트 (차분한 스팀식 남색 판 + 깎은 모서리)
colors:
  bg-0: "#0b1017"
  bg-1: "#0f161f"
  bg-2: "#141e2a"
  panel: "#172231"
  panel-2: "#1d2a3b"
  panel-3: "#223246"
  line: "#273649"
  line-2: "#34485f"
  input-well: "#0f1720"
  battle-floor: "#070b10"
  text: "#e3eaf1"
  text-2: "#a9b7c6"
  text-3: "#7d8ea1"
  play: "#6fb92a"
  play-hi: "#86cf3a"
  play-lo: "#457a17"
  cyan: "#3cc8ff"
  cyan-2: "#1d8fc4"
  cyan-bg: "rgba(60, 200, 255, 0.1)"
  enemy: "#ff5470"
  enemy-bg: "rgba(255, 84, 112, 0.1)"
  amber: "#f3b53f"
  amber-bg: "rgba(243, 181, 63, 0.12)"
typography:
  display:
    fontFamily: "Chakra Petch, Noto Sans KR, sans-serif"
    fontSize: "clamp(4rem, 11vw, 8.5rem)"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "0.06em"
  logo:
    fontFamily: "Chakra Petch, Noto Sans KR, sans-serif"
    fontSize: "clamp(2.6rem, 5vw, 4.4rem)"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "0.02em"
  headline:
    fontFamily: "Noto Sans KR, Malgun Gothic, Apple SD Gothic Neo, system-ui, sans-serif"
    fontSize: "clamp(1.6rem, 2.2vw, 2rem)"
    fontWeight: 900
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  title:
    fontFamily: "Noto Sans KR, Malgun Gothic, Apple SD Gothic Neo, system-ui, sans-serif"
    fontSize: "1.05rem"
    fontWeight: 700
  body:
    fontFamily: "Noto Sans KR, Malgun Gothic, Apple SD Gothic Neo, system-ui, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Noto Sans KR, Malgun Gothic, Apple SD Gothic Neo, system-ui, sans-serif"
    fontSize: "0.8rem"
    fontWeight: 700
  numeric:
    fontFamily: "Chakra Petch, Noto Sans KR, sans-serif"
    fontWeight: 700
    letterSpacing: "0.04em"
    fontFeature: "tnum"
  data-label:
    fontFamily: "Chakra Petch, Noto Sans KR, sans-serif"
    fontSize: "0.72rem"
    fontWeight: 600
    letterSpacing: "0.16em"
rounded:
  hairline: "2px"
  tile: "3px"
  pill: "10px"
  round: "50%"
  cut-card: "12px"
  cut-button: "8px"
spacing:
  page-x: "clamp(16px, 3.4vw, 48px)"
  xs: "6px"
  sm: "10px"
  md: "14px"
  lg: "18px"
  xl: "22px"
  card: "20px"
components:
  button-primary:
    backgroundColor: "{colors.play}"
    textColor: "#ffffff"
    rounded: "{rounded.cut-button}"
    padding: "0 18px"
    height: "42px"
  button-primary-hover:
    backgroundColor: "{colors.play-hi}"
  button-default:
    backgroundColor: "{colors.panel-3}"
    textColor: "{colors.text}"
    rounded: "{rounded.cut-button}"
    padding: "0 18px"
    height: "42px"
  button-info:
    backgroundColor: "{colors.cyan-bg}"
    textColor: "#cdefff"
    rounded: "{rounded.cut-button}"
    padding: "0 18px"
    height: "42px"
  button-danger:
    backgroundColor: "{colors.enemy-bg}"
    textColor: "#ffd1d9"
    rounded: "{rounded.cut-button}"
    padding: "0 18px"
    height: "42px"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.text-2}"
    rounded: "{rounded.cut-button}"
    padding: "0 18px"
    height: "42px"
  card:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.text}"
    rounded: "{rounded.cut-card}"
    padding: "20px"
  input:
    backgroundColor: "{colors.input-well}"
    textColor: "{colors.text}"
    rounded: "{rounded.hairline}"
    padding: "0 14px"
    height: "46px"
  chip:
    backgroundColor: "{colors.panel-2}"
    textColor: "{colors.text-2}"
    rounded: "{rounded.hairline}"
    padding: "0 12px"
    height: "30px"
  hand-slot-ready:
    backgroundColor: "{colors.panel-2}"
    textColor: "{colors.text}"
    rounded: "{rounded.cut-button}"
    padding: "8px 4px 6px"
  hand-slot-cooldown:
    backgroundColor: "{colors.bg-2}"
    textColor: "{colors.text-2}"
---

# Design System: War of Wizards

## Overview

**Creative North Star: "작전실의 1:1 결투장 (The Duel Briefing Room)"**

War of Wizards 웹 클라이언트는 스팀 게임 클라이언트처럼 차분한 남색 판 위에서 움직입니다. 화려함은 마법사 두 명(파랑 = 나, 빨강 = 상대)과 팀이 그린 마법진 그림이 맡고, 화면 틀(판, 버튼, 입력칸)은 조용히 물러나 있습니다. 사이버 느낌은 딱 하나, **깎은 모서리**(clip-path 로 오른쪽 위·왼쪽 아래를 비스듬히 자른 판과 버튼)와 가는 선으로만 냅니다.

색은 장식이 아니라 **뜻**입니다. 초록은 "지금 누를 주요 행동", 청록은 "나 · 정보", 분홍빨강은 "상대 · 위험", 호박색은 "고위력 마법". 그 외의 모든 것은 남색 단계와 회청색 글자로 처리합니다. 처음 보는 사람도 어느 화면에서든 파란 마법사 대 빨간 마법사 그림과 "룬 그리기 → 준비 → 조준 → 발동" 네 단계를 보고 무슨 게임인지 알 수 있어야 합니다.

확인된 거부 목록: 보라색 네온, 빛 번짐(glow halo), 장식용 격자 · 스캔라인, 판 옆 색 띠(side stripe), 제목 위 작은 머리말(kicker · eyebrow), Cinzel 같은 판타지 장식 글꼴, 웹 화면의 전장 지도.

**Key Characteristics:**
- 남색 8단계 바탕 + 1px 선(inset box-shadow)으로 나눈 판
- 판 12px · 버튼 8px 깎은 모서리, 판 오른쪽 위에 대각선 표시선 하나
- 색 = 뜻 (초록 행동 / 청록 나 / 분홍빨강 상대 / 호박 고위력)
- 한글은 Noto Sans KR, 숫자 · 코드 · 영어 표시는 Chakra Petch
- 모든 화면 머리글 왼쪽에 "WAR OF WIZARDS" 글자 로고 (누르면 로비)
- 마법사 1:1 그림이 로그인 · 로비 · 매칭 · 카운트다운 · 전투 · 결과 · 404 를 관통

## Colors

남색 단계 위에 뜻이 있는 색 네 가지만 얹는, 채도 낮은 바탕 + 또렷한 신호색 팔레트입니다. 모든 값은 `Backend/static/client/wow.css` 의 `:root` 변수가 원본입니다.

### Primary
- **출격 초록 (Sortie Green)** (`--play`, 밝은 끝 `--play-hi`, 아래 받침선 `--play-lo`): 화면마다 하나뿐인 주요 행동 버튼(`.btn.play`: 로그인, 랭킹전 매칭, 덱 저장, 수령, 대전 시작, 완료 …). 위→아래 `--play-hi`→`--play` 그라디언트, 아래 3px `--play-lo` 받침선, 글자는 흰색. 카운트다운의 "GO" 도 `--play-hi`.

### Secondary
- **아군 청록 (Ally Cyan)** (`--cyan`, 진한 `--cyan-2`, 옅은 판 `--cyan-bg`): 나 · 정보. 내 체력바, 내 쪽 "YOU" 라벨, 승리 글자(VICTORY), 선택된 탭 밑줄, 고른 슬롯 테두리(2px), 지금 단계(`.cast-steps li.now`), 정보 버튼(`.btn.info`) · 알림(`.note.info`), 링크, 덱 슬롯 번호 배지(`--cyan-2`), 랭킹 표에서 내 줄(`tr.is-me`).

### Tertiary
- **적군 분홍빨강 (Foe Rose)** (`--enemy`, 옅은 판 `--enemy-bg`): 상대 · 위험. 상대 체력바, "ENEMY" 라벨, 패배 글자(DEFEAT), 숨은 상대 실루엣의 1.5px 테두리, 위험 버튼(`.btn.danger`), 오류 알림, 필수 표시(`.req`), 안 읽음 배지(`.badge`), 404 의 가운데 숫자.
- **고위력 호박 (High-Power Amber)** (`--amber`, 옅은 판 `--amber-bg`): 고위력 마법(H01–H06)에만. 마법진 둥근 슬롯의 2px 호박 테두리 + 오른쪽 위 마름모(`.sk.high`), 전투 손패의 호박 테두리 · 글자(`.hslot.high`). 예외적으로 기본 경고 알림(`.note`)과 대기 중 점(`.dot.wait`)도 호박색입니다.

### Neutral
- **심야 남색 단계** (`--bg-0` → `--bg-1` → `--bg-2` → `--panel` → `--panel-2` → `--panel-3`): 가장 어두운 바탕에서 가장 밝은 판까지. 페이지 바탕 `--bg-0`, 위쪽 260px 까지만 `#132030` 으로 살짝 밝아지는 `.backdrop` 그라디언트, 카드 `--panel-2`→`--panel` 그라디언트, 기본 버튼 `--panel-3`.
- **가는 선** (`--line`, 강조 `--line-2`): 모든 구분선과 판 테두리. 테두리는 `border` 가 아니라 `box-shadow: inset 0 0 0 1px` 로 그립니다(깎은 모서리 clip-path 와 함께 잘리도록).
- **입력 우물** (`input-well` #0f1720) · **전투 바닥** (`battle-floor` #070b10): 입력칸 · 체력바 안쪽, 전투 화면 전체 바탕.
- **글자 3단** (`--text` 본문 · 제목, `--text-2` 보조 설명, `--text-3` 라벨 · 흐린 정보).
- **랭킹 메달** (1위 `#ffd166`, 2위 `#d9e2ec`, 3위 `#e09f5e`): 랭킹 순위 숫자(`.rank-1/2/3`)에만 쓰는 메달 색. 다른 곳에 강조색으로 가져가지 않습니다.

### Named Rules
**색 = 뜻 규칙 (The Meaning-Only Rule).** 초록 = 주요 행동, 청록 = 나 · 정보, 분홍빨강 = 상대 · 위험, 호박 = 고위력. 새 화면에서 "예뻐 보이려고" 색을 고르지 않습니다. 뜻이 없으면 남색과 회색 글자입니다.

**초록 하나 규칙 (The One Green Action Rule).** 꽉 찬 초록 버튼(`.btn.play`)은 한 화면(한 상태)에 하나. 나머지 행동은 `.btn`(기본), `.btn.ghost`, `.btn.info`, `.btn.danger` 로 내립니다. 작은 상태 표시(접속 중 점 `.dot.on`, 켜진 스위치, 성공 알림 `.toast.ok`, `.ok-text`)의 초록은 "켜짐 · 성공" 을 뜻하는 작은 표시로만 쓰고, 꽉 찬 초록 블록은 만들지 않습니다.

**READY 예외 규칙 (The READY Slot Exception).** 전투 HUD 손패 슬롯(`.hslot`)의 사용 가능 상태는 초록입니다: **1px 초록 테두리**(`rgba(111,185,42,.6)`) + **초록 "READY" 글자**(`--play-hi`). 팀의 Unity 게임 영상("4조 XR")이 초록 READY 슬롯을 쓰기 때문에 사용자가 정한 예외입니다. 슬롯을 초록으로 꽉 채우는 일은 절대 없습니다. 고위력 슬롯은 같은 자리를 호박색으로(`READY 2/3`), 재사용 대기(`.cool`)는 회색 + 흑백 마법진, 소진(`.spent`)은 55% 투명.

## Typography

**Display Font:** Chakra Petch (뒤에 Noto Sans KR, sans-serif) — `--tech`
**Body Font:** Noto Sans KR (뒤에 Malgun Gothic, Apple SD Gothic Neo, system-ui) — `--ui`
**Label/Mono Font:** Chakra Petch + `font-variant-numeric: tabular-nums` (`.num`, `.tech`)

**Character:** 한글은 굵기 차이(400 본문 / 700 라벨 / 900 제목)로 위계를 만드는 단정한 고딕, 숫자와 영어는 각진 테크 글꼴 Chakra Petch 가 게임 계기판 느낌을 냅니다. index.html 이 Google Fonts 에서 Chakra Petch 500/600/700, Noto Sans KR 400/500/700/900 을 불러옵니다.

### Hierarchy
- **Display** (Chakra Petch 700, `clamp(4rem, 11vw, 8.5rem)`, 줄높이 0.95, 자간 0.06em): 결과 화면 VICTORY / DEFEAT(`.result-word`). 같은 계열로 카운트다운 숫자(`.count-num`, `clamp(5rem, 12vw, 9rem)`), 큰 타이머(`.big-timer`), 오류 코드(`.error-code`).
- **Logo** (Chakra Petch 700, `clamp(2.6rem, 5vw, 4.4rem)`, 0.95): 로그인 왼쪽 큰 로고(`.logo`, "WIZARDS" 만 청록).
- **Headline** (Noto Sans KR 900, `clamp(1.6rem, 2.2vw, 2rem)`, 1.2, -0.01em): 화면 머리글 제목(`.ph h1`). 비슷한 900 굵기 제목: 로비 대전 카드 `1.9rem`, 마법서 상세 · 편지 `1.6rem`, 로그인 카드 `1.7rem`.
- **Title** (700, `1.05rem`): 카드 머리 제목(`.h2`, 옆 개수는 Chakra Petch `--text-3`).
- **Body** (400, 15px, 1.6): 기본 본문. `word-break: keep-all` 로 한글 단어를 끊지 않습니다. 큰 글씨 설정(`html.ui-large`)은 16.5px. 편지 본문은 최대 65ch.
- **Label** (700, `0.8rem`, `--text-3`): 판 안의 구역 이름(`.label`: "내 마법서", "룸 코드", "시전 상태"). 입력 라벨은 `0.84rem` `--text-2`.
- **Data label** (Chakra Petch 600, `0.72rem`, 자간 0.16em, 대문자): 표 머리(`.table th`)와 YOU / ENEMY 같은 편 표시(`.side-label` 0.3em, `.seat .side` 0.2em). 데이터를 담을 때만 씁니다.

### Named Rules
**숫자는 테크 규칙 (The Numbers-Are-Tech Rule).** 점수, 체력, 시간, 코드, 스킬 번호(S01 · H02)는 Chakra Petch(`.num` / `.tech`)로. 한글 문장은 절대 Chakra Petch 로 쓰지 않습니다.

**머리말 금지 규칙 (The No-Kicker Rule).** 제목 위에 작은 대문자 머리말(eyebrow · kicker)을 달지 않습니다. 화면 이름은 머리글의 h1 하나, 게임 이름은 왼쪽 글자 로고가 맡습니다. 작은 자간 라벨은 YOU / ENEMY, 표 머리처럼 정보를 담을 때만.

## Layout

- **화면 틀** (`.screen`): 세로 flex. 머리글 `.ph` → 스크롤되는 본문 `.body` (padding `26px var(--pad-x) 40px`, 안쪽 최대 폭 `.wrap` 1320px) → 필요하면 아래 버튼 줄 `.foot`. 데스크톱(>960px)에서는 `body` 가 스크롤되지 않고 `.body` 만 스크롤됩니다.
- **머리글 패턴** (`headerHTML(title, { right, back })`, core.js): 왼쪽에 글자 로고 `WAR OF <b>WIZARDS</b>`(`.wordmark`, Chakra Petch 700 0.95rem, `--text-2`, "WIZARDS" 만 청록, 오른쪽 1px `--line-2` 세로 구분선, 누르면 `#/lobby`) + h1 화면 이름. 오른쪽에 화면별 버튼과 기본 `로비` 고스트 버튼(`back: false` 로 끔, 로비에서만). 위쪽 50px 여백은 왼쪽 위 DEV 화면 목록 자리입니다. 아래 1px `--line` 선.
- **가로 여백**: `--pad-x: clamp(16px, 3.4vw, 48px)` 하나로 모든 화면 좌우를 맞춥니다.
- **간격 리듬**: 6 / 10 / 14 / 18 / 22px 단계, 카드 안쪽 20px, 카드 머리 `14px 20px`, 목록 줄 `10px 20px`(최소 높이 58px).
- **화면 구성**: 로비 `1.2fr : 1fr`(왼쪽 대전 카드, 오른쪽 2×3 타일 + 룬 연습장 줄), 마법서 `1fr : 400px` 옆 패널, 우편함 `400px : 1fr`, 마이페이지 `340px : 1fr`, 로그인 `1.1fr : 0.9fr`(왼쪽 소개 그림, 오른쪽 폼, 최소 400px), 전투 `240px : 1fr : 280px`(왼쪽 시전 단계, 가운데 Unity 무대, 오른쪽 룬 패드) + 아래 손패 8칸. 매칭 · 카운트다운 · 결과 · 오류는 가운데 정렬 화면(`.focus-screen`, `.vs-screen`, `.error-page`).
- **반응형**: ≤1180px 옆 패널 축소(마법서 340px, 전투 `200px : 1fr : 240px`). ≤960px 모든 2열이 1열, 문서 전체 스크롤, 전투 왼쪽 패널 숨김, 머리글 줄바꿈(위 여백 58px), 표의 `.hide-sm` 열 숨김. ≤560px 타일 2열 유지 · 대전 버튼 1열 · 좌석 1열 · 손패 슬롯 22% 폭.

## Elevation & Depth

평평한 판 + 남색 단계로 깊이를 냅니다. 그림자는 바닥에 뜬 것(알림, 확인 창)에만 쓰고, 빛 번짐은 쓰지 않습니다. 판의 "윤곽"은 모두 안쪽 1px 선(inset box-shadow)이고, 위로 갈수록 밝은 남색(`--panel-3` > `--panel-2` > `--panel` > `--bg-2`)이 앞에 있는 것입니다.

### Shadow Vocabulary
- **뜬 판** (`--shadow: 0 10px 30px rgba(0, 0, 0, 0.35)`): 알림(`.toast`)과 확인 창(`dialog.confirm`)에만. 항상 inset 1px 선과 함께.
- **마법사 바닥 그림자** (`filter: drop-shadow(0 10px 14px rgba(0, 0, 0, 0.5))`): 마법사 SVG(`.wiz svg`)를 바닥에 붙이는 어두운 그림자.
- **초록 버튼 글자 그림자** (`text-shadow: 0 1px 1px rgba(0, 0, 0, 0.3)`): 초록 위 흰 글자 가독성용.
- **입력 초점 고리** (`0 0 0 3px rgba(60, 200, 255, 0.15)` + 1px 청록 선): 입력칸 초점 표시. 그 밖의 초점은 전역 `outline: 2px solid #fff; outline-offset: 2px`.

### Named Rules
**빛 번짐 금지 규칙 (The No-Halo Rule).** UI 틀(버튼, 판, 글자)에 색 있는 빛 번짐 그림자를 넣지 않습니다. 빛나는 것은 그림 안의 것뿐: 마법사 지팡이 구슬(SVG 방사 그라디언트)과 룬 궤적, 팀 마법진 PNG. 듀얼 무대의 양쪽 바닥 색 번짐(왼쪽 청록 10%, 오른쪽 분홍빨강 10%의 radial-gradient)은 "나 vs 상대" 를 알리는 배경이지 테두리 광채가 아닙니다.

## Shapes

- **깎은 모서리** (clip-path): 카드 `.card` 는 오른쪽 위 · 왼쪽 아래를 `--cut`(12px)만큼 깎고, 오른쪽 위 깎인 자리에 `--line-2` 색 대각선 하나(`.card::after`)를 그립니다. 버튼 `.btn` 은 같은 두 모서리를 8px. 마법서 스킬 칸(`.skill`)은 오른쪽 위만 10px, 전투 손패(`.hslot`)는 오른쪽 위만 8px.
- **작은 둥근 모서리**: 입력칸 · 칩 · 스위치 · 키 표시 2px, 룬 타일 · DEV 목록 3px, 안 읽음 배지 10px 알약.
- **원**: 마법진은 언제나 원 안에(`.sk img`, `.hslot img`, `.result-circle`, `border-radius: 50%`, 검정 바탕). 상태 점, 매칭 스캔 원도 원.
- **기타 고유 형태**: 프로필 아바타는 육각형(clip-path), 고위력 표시는 작은 호박 마름모, 게임 설명 4단계 사이는 45° 돌린 작은 마름모.
- 선은 `border` 대신 `box-shadow: inset 0 0 0 1px` (clip-path 에 맞게 잘리도록). 빈 칸(빈 덱 슬롯, 빈 좌석)은 1px 점선 outline.

## Components

### Buttons
차분하고 단단하게. 판과 같은 남색에 깎은 모서리, 누르면 1px 내려갑니다.
- **Shape:** 왼쪽 아래 · 오른쪽 위를 8px 깎은 모서리 (clip-path), 테두리는 `--c` 변수로 바꾸는 inset 1px 선.
- **크기:** 기본 높이 42px · 좌우 18px · 0.92rem 700. `.sm` 34px, `.lg` 54px · 1.05rem, `.xl` 64px · 1.25rem(로비 랭킹전 매칭), `.block` 가득 폭, `.icon-only` 36px.
- **Primary** (`.btn.play`): 초록 그라디언트 + 흰 글자 + 아래 3px 진초록 받침. 화면당 하나.
- **Hover / Focus:** 기본 버튼은 배경 `#2a3d55` · 선 `#4b6480`, 초록은 `#98df48`→`--play-hi`. 0.15s 전환, `:active` 에서 `translateY(1px)`, `:disabled` 40% 투명. 초점은 전역 흰 2px outline.
- **Secondary:** `.btn`(기본, `--panel-3`), `.btn.info`(청록 10% 판 + 청록 선, 정보 · 이동), `.btn.danger`(분홍빨강 판, 탈퇴 · 기권 등 위험), `.btn.ghost`(투명, `--text-2`, 로비로 · 취소), `.btn.text`(청록 글자 링크 버튼).
- **바쁨:** `busy(btn, fn)` 이 버튼을 잠그고 `.spin` 원 + "잠시만요…" 로 바꿉니다.

### Chips
- **Style:** 높이 30px, 2px 모서리, `--panel-2` + inset 1px `--line`, 0.82rem 700 `--text-2`.
- **State:** `.chip.cyan`(내 정보 · 내 순위), `.chip.amber`(고위력), `.chip.red`(상대 · 위험). `.chip.green` 은 CSS 에 있으나 현재 화면에서 쓰지 않습니다.

### Cards / Containers
- **Corner Style:** 12px 깎은 모서리 + 오른쪽 위 대각선 표시선 (`.card`).
- **Background:** `--panel-2`→`--panel` 세로 그라디언트, `.card.flat` 은 `--panel` 한 색.
- **Shadow Strategy:** 없음. inset 1px `--line` 윤곽만 (Elevation 참고).
- **Internal Padding:** `.card-pad` 20px, 머리 `.card-head` `14px 20px` + 아래 1px 선. 판 안의 작은 우물(덱 줄, 룬→마법진, 첨부)은 `--bg-2` + inset 1px 선, 모서리 깎지 않음.
- **통계 칸** (`.facts`, `.trio`, `.stat-row`): 1px `--line` 간격으로 붙인 격자, 숫자는 Chakra Petch.

### Inputs / Fields
- **Style:** 높이 46px, 2px 모서리, `#0f1720` 우물 + inset 1px `--line-2`, placeholder `#5e6f82`. 왼쪽 아이콘이 있으면 왼쪽 여백 40px(`.input-wrap`).
- **Focus:** 청록 1px 선 + 3px 청록 15% 고리, 배경 `#111b26`.
- **Error / Disabled:** `aria-invalid="true"` 이면 분홍빨강 1px 선, 아래 `.err`(#ff9fb0). 룸 코드 입력 `.code-input` 은 Chakra Petch 1.9rem, 자간 0.4em, 가운데.
- **스위치 · 체크:** 스위치는 네모(2px) 손잡이, 켜지면 초록 판 + `--play-hi` 손잡이. 체크박스 `accent-color: --play`, 범위 슬라이더 `accent-color: --cyan`.

### Navigation
- **머리글** (`.ph`): Layout 의 머리글 패턴 그대로. 글자 로고가 곧 홈 링크.
- **탭** (`.tabs`): 높이 40px, `--text-3` 700, 선택되면 흰 글자 + 2px 청록 밑줄.
- **로비 타일** (`.tile` in `.card`): 청록 아이콘 26px + 이름(1.1rem) + 한 줄 설명, hover 시 판이 `#23344a` 쪽으로 밝아짐, 오른쪽 위 안 읽음 배지.
- **DEV 화면 목록** (`.dev`): 왼쪽 위 고정, 발표 · 시험용. 현재 화면은 `--cyan-2` 판.
- **전투 줄 고르기** (`.lanes`): L / C / R, 눌린 칸은 청록 꽉 채움 + 어두운 글자.

### 마법사 1:1 그림 (Signature)
`duelHTML({ foe, st })` / `wizardSVG(side)` (core.js). 파란 마법사(나, 왼쪽) — "VS"(Chakra Petch) — 빨간 마법사(상대, 좌우 반전). 같은 그림이 로그인 소개, 로비 대전 카드, 매칭, 카운트다운, 전투 무대(Unity 대신), 결과, 404 에 나옵니다.
- **상대 미정** (`foe: false` → `.wiz.unknown`): 검은 실루엣 + 1.5px 분홍빨강 윤곽, 구슬 · 궤적 숨김. 매칭 중(`st: "search"`)에는 깜빡임.
- **드러남** (카운트다운 `.vs-side:last-child .fig`): `reveal` 0.5s `--ease`, 0.1s 늦게 — 검은 실루엣에서 빨간 마법사로 바뀝니다. 서버가 상대를 정한 순간을 보여주는 이 시스템의 대표 움직임입니다.
- **결과:** 진 쪽은 `.lost`(흑백 + 어둡게).
- **무대 배경:** `.battle-card .hero` · `.unity-stage` 는 왼쪽 아래 청록 10%, 오른쪽 아래 분홍빨강 10% radial 번짐 + `#172231`→`#0f161f`. 나중에 Unity WebGL 은 `.unity-stage iframe`(absolute, 꽉 채움, 검정 바탕)으로 이 자리에 들어가고, HUD(체력바 · 시간 · 시전 단계 · 룬 패드 · 손패)는 웹이 그대로 감쌉니다. 전장 지도는 웹에 그리지 않습니다.

### 마법 그림 (스킬 아이콘 · 룬)
팀이 만든 PNG(`assets/skills/{rune,rune_line,circle}/<코드>.png`)를 **고치지 않고** 씁니다.
- **마법진** (`skHTML(id, size)` → `.sk`): 둥근 슬롯(검정 원) 안에, 기본 2px `#3a4b60` 고리. 고위력은 **호박 고리 + 오른쪽 위 호박 마름모**(`.sk.high`). 못 쓰는 상태는 `.sk.dim`(흑백 · 어둡게).
- **룬** (`runeHTML(id, size, line)` → `.rune-tile`): 어두운 타일(`#1f3045`→`#142030` radial, inset 1px `--line-2`, 3px 모서리) 위에 **밝은 선**(#cdefff)으로. PNG 를 CSS mask 로 써서 색만 입히고 모양은 그대로.
- **룬 → 마법진 짝**: 룬이 있는 곳에는 마법진이 함께 (`.rune-to-circle`, 로그인의 `.pair-mini`). 마법서 상세는 104px.
- **룬 패드** (`.pad`): 어두운 정사각형, 안쪽 점선 원(청록 18%), 따라 그리기 그림자는 회청색 20%.
- **전투 손패** (`.hslot`): Colors 의 READY 예외 규칙 참고. 고른 슬롯은 2px 청록 + 6px 위로.

### 상태 표시
- **알림** (`.toast`): 오른쪽 아래, 기본은 청록 선, `.error` 진분홍 판, `.ok` 진초록 판, 0.25s 아래에서 올라옴, 3.8초 뒤 사라짐.
- **안내 상자** (`.note`): 기본 호박(주의), `.info` 청록, `.red` 분홍빨강. 테두리는 사방 1px — 왼쪽 색 띠 아님.
- **점** (`.dot`): 회색 · `.on` 초록(접속 · 준비) · `.bad` 분홍빨강 · `.wait` 호박 깜빡임.
- **체력바** (`.hp-bar`): 높이 10px, 나는 청록 왼쪽에서, 상대는 분홍빨강 오른쪽에서, `transform: scaleX(var(--hp))` 0.4s. 색약 모드(`html.colorblind`)에서는 상대 체력바만 45° 줄무늬 — 색 없이도 구분되게 하는 의도된 무늬입니다.

### 움직임
- 기본 곡선 `--ease: cubic-bezier(0.16, 1, 0.3, 1)`, 상태 전환 0.12–0.25s.
- 카운트다운 숫자 `pop` 0.9s(1.4배에서 줄어듦), 상대 `reveal` 0.5s, 시전 효과 `fx` 1.4s, 연습장 `castfx` 1.1s, 매칭 스캔 원 2.2s 회전, 마법사 구슬 2.6s 숨쉬기, 룬 궤적 3.4s.
- `prefers-reduced-motion: reduce` 이면 모든 애니메이션 · 전환이 0.01ms 한 번으로 끝납니다(최종 상태만 보임). 새 움직임도 이 규칙 아래에 들어가야 합니다.

### 화면 위치
| 화면 | 파일 | 주소 |
|---|---|---|
| 로그인 · 가입 · 비밀번호 | `Backend/static/client/pages/auth.js` | `#/login`, `#/signup` |
| 메인 로비 | `pages/lobby.js` | `#/lobby` |
| 마법서(덱) | `pages/spellbook.js` | `#/spellbook` |
| 마이페이지 | `pages/mypage.js` | `#/mypage` |
| 환경설정 | `pages/settings.js` | `#/settings` |
| 친구 | `pages/friends.js` | `#/friends` |
| 친선전 룸 | `pages/room.js` | `#/room` |
| 우편함 · 공지 | `pages/mailbox.js` | `#/mailbox` |
| 랭킹 | `pages/ranking.js` | `#/ranking` |
| 매칭 대기 | `pages/queue.js` | `#/queue` |
| 카운트다운 | `pages/countdown.js` | `#/countdown` |
| 전투 HUD | `pages/battle.js` | `#/battle` |
| 결과 | `pages/result.js` | `#/result` |
| 룬 연습장 | `pages/runes.js` | `#/runes` |
| 관리자 | `pages/admin.js` | `#/admin` |
| 없는 화면(앱 안) | `pages/notfound.js` | 그 외 `#/…` |
| 서버 404 · 5xx | `Backend/static/404.html`, `50x.html` | (wow.css 를 못 읽어도 보이게 최소 인라인 스타일 포함) |

공용 조각(머리글, 아이콘, 마법사, 스킬 · 룬 그림, 알림, 확인 창)은 `client/core.js`, 모든 스타일은 `client/wow.css`, 화면 이동은 `client/main.js`.

## Do's and Don'ts

### Do:
- **Do** 새 색이 필요하면 먼저 `:root` 변수(`--play`, `--cyan`, `--enemy`, `--amber`, 남색 단계)에서 고르고, 뜻(행동 / 나 / 상대 / 고위력)으로 정하세요.
- **Do** 새 화면은 `headerHTML("화면 이름")` 으로 시작해 왼쪽 "WAR OF WIZARDS" 글자 로고와 h1 하나를 유지하세요.
- **Do** 꽉 찬 초록 버튼(`.btn.play`)은 한 화면에 하나만, 나머지는 기본 · ghost · info · danger 로.
- **Do** 판은 `.card`(12px 깎은 모서리), 버튼은 `.btn`(8px), 선은 `box-shadow: inset 0 0 0 1px var(--line)` 으로.
- **Do** 숫자 · 코드 · 스킬 번호는 Chakra Petch(`.num`, `.tech`), 한글은 Noto Sans KR.
- **Do** 마법진은 둥근 슬롯 안에(`skHTML`), 룬은 어두운 타일 위 밝은 선으로(`runeHTML`), 고위력은 호박 고리. 팀 PNG 는 그대로 두고 파일 교체로만 바꾸세요.
- **Do** 대전이 나오는 화면에는 `duelHTML()` 마법사 1:1 을 쓰고, 상대가 정해지기 전에는 `foe: false` 실루엣으로.
- **Do** Unity WebGL 은 `.unity-stage` 안의 iframe 으로 넣고 웹 HUD(체력바, 시전 단계, 룬 패드, 손패)는 둘레에 그대로 두세요.
- **Do** 새 애니메이션은 `--ease` 를 쓰고 `prefers-reduced-motion` 에서 꺼져도 화면이 말이 되게 하세요.

### Don't:
- **Don't** 제목 위에 작은 대문자 머리말(kicker · eyebrow)을 달지 마세요. 게임 이름은 머리글 글자 로고가 맡습니다.
- **Don't** 버튼 · 판 · 글자에 색 있는 빛 번짐(glow halo, 색 box-shadow · text-shadow)을 넣지 마세요.
- **Don't** 판 왼쪽 · 오른쪽에 색 띠(side stripe, 굵은 border-left)를 넣지 마세요. 강조는 사방 1px 선이나 옅은 색 판으로.
- **Don't** 보라색 · 네온 · 장식 격자 · 스캔라인을 쓰지 마세요.
- **Don't** 노란 신호색(예전 `--signal`)을 되살리지 마세요. 호박은 고위력 · 주의 전용, 메달 색은 랭킹 순위 전용입니다.
- **Don't** READY 슬롯을 초록으로 꽉 채우지 마세요(1px 선 + 글자만). 손패 밖에서 READY 예외를 다른 상태 표시로 넓히지 마세요.
- **Don't** 웹에 전장 지도를 그리지 마세요. 전투는 마법사 1:1 무대 + Unity 화면입니다.
- **Don't** Cinzel 같은 판타지 장식 글꼴이나 시스템 기본 글꼴을 제목 글꼴로 쓰지 마세요.
- **Don't** 팀 마법 그림을 다시 그리거나 색을 바꾸거나 자르지 마세요.
