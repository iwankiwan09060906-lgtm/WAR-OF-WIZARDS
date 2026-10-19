// War of Wizards — 게임 규칙 · 스킬 19종 (서버 wow_server.py 의 SKILLS 와 같은 내용)

export const KINDS = {
  projectile: { label: "투사체", aim: "열 지정", needsAim: true },
  area: { label: "범위", aim: "범위 지정 (열+깊이)", needsAim: true },
  beam: { label: "관통 빔", aim: "열 지정", needsAim: true },
  buff: { label: "버프", aim: "즉시 발동", needsAim: false },
  summon: { label: "소환", aim: "소환 라인 (아군)", needsAim: true },
  install: { label: "설치", aim: "설치 구역 (타워·넥서스 앞)", needsAim: true },
  global: { label: "전역", aim: "즉시 발동", needsAim: false },
};

// [코드, 이름, 등급(normal|high), 종류, 효과, 룬 그림 설명]
const RAW = [
  ["S01", "연막", "normal", "projectile", "적중 시 상대 명중률 감소 및 시야 외곽 안개 부여", "눈"],
  ["S02", "독 장판", "normal", "area", "구역 내 초당 지속 피해 누적", "독 문양"],
  ["S03", "파이어볼", "normal", "area", "소범위 집중 즉발 피해", "왕관"],
  ["S04", "화살 세례", "normal", "area", "광범위 다수 분산 피해", "세 갈래 화살"],
  ["S05", "토네이도", "normal", "projectile", "경로 상 적 유닛 넉백 및 피해", "소용돌이"],
  ["S06", "번개", "normal", "area", "지정 위치 타격 및 스플래시 피해", "번개"],
  ["S07", "총난사", "normal", "projectile", "열 전방 유닛부터 순차 연사 타격", "가로줄 5개"],
  ["S08", "방어 쉴드", "normal", "buff", "피해 흡수 쉴드 생성 (피격 없을 시 HP 회복)", "방패"],
  ["S09", "타워 버프", "normal", "buff", "본인 타워 방어력/공격력 일시 강화 (타워 파괴 시 비활성화)", "#"],
  ["S10", "집중", "normal", "buff", "다음 공격 1회의 명중률 및 공격력 대폭 증가", "위쪽 화살표"],
  ["S11", "도끼 전사", "normal", "summon", "근접 공격형 소환수 소환", "X"],
  ["S12", "마법사", "normal", "summon", "원거리 공격형 소환수 소환", "육각별"],
  ["S13", "터렛", "normal", "install", "적 유닛 요격용 고정 포탑 설치 (진영당 1기)", "I"],
  ["H01", "프리즈", "high", "global", "적 전체 미니언 및 소환수 5초간 행동 정지", "원 안 모래시계"],
  ["H02", "레이저 빔", "high", "beam", "0.5초 예고선 후 열 전체 관통 타격 (유닛 및 끝의 플레이어)", "별표 *"],
  ["H03", "흡혈", "high", "projectile", "적 플레이어/유닛 타격 시 실 피해량 비례 HP 회복", "심장 박동 하트"],
  ["H04", "반사 결계", "high", "buff", "상대 플레이어의 스킬 공격 1회 무효화 및 일부 피해 반사", "원 + 위 화살표"],
  ["H05", "분노", "high", "global", "필드 내 기존 아군 유닛들의 공격력/공격속도 증가", "화난 얼굴"],
  ["H06", "소환탑", "high", "install", "주기적으로 소형 근접 소환수를 생성하는 탑 설치", "집"],
];

// 그림 주소는 절대 주소로: CSS 변수(--rune) 안의 url() 이 어느 파일 기준으로 풀려도 같은 그림을 가리키게
const ASSETS = new URL("assets/skills/", document.baseURI).href;
export const SKILLS = RAW.map(([id, name, tier, kind, effect, rune]) => ({
  id, name, tier, kind, effect, rune,
  high: tier === "high",
  circle: `${ASSETS}circle/${id}.png`,
  runeImg: `${ASSETS}rune/${id}.png`,
  runeLine: `${ASSETS}rune_line/${id}.png`,
}));
export const SKILL = Object.fromEntries(SKILLS.map((s) => [s.id, s]));

export const RULES = {
  deckSize: 8,
  highLimit: 2,
  highUsesPerGame: 3,
  cooldown: { normal: 5, high: 20 },
  aimTimeout: 5,
  holdToAim: 1,
  queueBotTimeout: 15,
  countdown: 3,
  maxHp: 1000,
};

export const MODE_LABEL = { ranked: "랭킹전", friendly: "친선전", bot: "봇 연습전" };

// 덱 규칙 확인 → 문제 문장 또는 null
export function deckProblem(deck) {
  if (deck.length !== RULES.deckSize) return `덱은 정확히 8장이어야 합니다. (현재 ${deck.length}장)`;
  if (new Set(deck).size !== deck.length) return "같은 스킬을 중복해서 넣을 수 없습니다.";
  if (deck.some((id) => !SKILL[id])) return "존재하지 않는 스킬이 포함되어 있습니다.";
  if (deck.filter((id) => SKILL[id].high).length > RULES.highLimit) return "고위력 스킬은 덱에 최대 2장까지 넣을 수 있습니다.";
  return null;
}

// 레이팅 → 등급 (카드 희귀도처럼)
export function tierOf(rating = 1000) {
  if (rating >= 1500) return { name: "대마법사", key: "legend", frame: "holo" };
  if (rating >= 1300) return { name: "마법사", key: "epic", frame: "gold" };
  if (rating >= 1100) return { name: "숙련 마법사", key: "rare", frame: "" };
  if (rating >= 900) return { name: "견습 마법사", key: "uncommon", frame: "" };
  return { name: "입문자", key: "common", frame: "" };
}

export const TIPS = [
  "적 타워가 살아있는 동안에는 상대 플레이어에게 20%의 피해만 들어갑니다.",
  "플레이어 스킬은 타워·넥서스를 공격할 수 없어요. 유닛으로 구조물을 밀어붙이세요.",
  "고위력 스킬은 게임당 3회까지만 쓸 수 있습니다. 결정적인 순간에 아껴두세요.",
  "왼손을 옆으로 휘두르면 좌/중/우 열을 한 칸씩 순간이동합니다.",
  "적 타워를 부수면 공격력이 영구 +20%, 넥서스를 부수면 +40% 강해집니다.",
  "룬은 쓸 수 있는 슬롯 안에서만 찾습니다. 쿨타임 중인 카드는 그려도 인식되지 않아요.",
];

export const DEFAULT_SETTINGS = {
  camSensitivity: 65,
  comfortVignette: true,
  snapTurning: false,
  sfx: 80,
  bgm: 45,
  spatialAudio: true,
  gestureSensitivity: 70,
  holdFrames: 15,
  haptics: true,
  subtitles: false,
  uiLarge: true,
  colorblind: false,
};
