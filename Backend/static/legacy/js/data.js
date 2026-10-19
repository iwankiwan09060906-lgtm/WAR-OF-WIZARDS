/* ==========================================================================
   게임 데이터: 규칙 상수 + 스킬 19종 + 덱 검증
   (서버 DeckService · skills 테이블과 같은 규칙)
   새 스킬을 추가하려면 SKILLS 배열에 한 줄 추가하고, 전투 효과는 js/battleSim.js 의 cast() 에 case 를 추가하세요.
   ========================================================================== */
window.WOW = window.WOW || {};

(function (WOW) {
  // [명세] = 기능명세서에 수치가 있는 값 / [TBD] = 밸런스 회의에서 확정 필요
  WOW.RULES = {
    highUsesPerGame: 3,                                   // [명세] 고위력 스킬별 게임당 3회
    cooldown: { normal: 5, high: 20 },                    // [명세]
    wave: { interval: 10, melee: 2, ranged: 1, bruteEvery: 3, brute: 1, unitCap: 20 }, // [명세]
    defense: { towerAliveTakeRatio: 0.2, regenInterval: 10, regenAmount: 20 },        // [명세]/[TBD]
    permBuff: { towerBreak: 0.2, nexusBreak: 0.4 },       // [명세]
    phase: { interval: 300, cooldownCut: 0.1, atkBoost: 0.1 },
    afk: { warnAfter: 20, loseAfter: 30 },                // [명세]
    aimTimeout: 5,                                        // [명세]
    queue: { botTimeout: 15 },                            // [TBD]
    countdown: 3,                                         // [명세]
    player: { maxHp: 600 },                               // [TBD]
    structure: { tower: 300, nexus: 500 },                // [TBD]
  };
  WOW.COLUMN_LABEL = ['좌', '중', '우'];

  WOW.KIND_META = {
    projectile: { label: '투사체', aim: '열 지정', needsAim: true },
    area:       { label: '범위', aim: '범위 지정 (열+깊이)', needsAim: true },
    beam:       { label: '관통 빔', aim: '열 지정', needsAim: true },
    buff:       { label: '버프', aim: '즉시 발동', needsAim: false },
    summon:     { label: '소환', aim: '소환 라인 (아군)', needsAim: true },
    install:    { label: '설치', aim: '설치 구역 (타워·넥서스 앞)', needsAim: true },
    global:     { label: '전역', aim: '즉시 발동', needsAim: false },
  };

  function glowOf(hex, a) {
    const n = parseInt(hex.slice(1), 16);
    return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + (a == null ? 0.65 : a) + ')';
  }
  function s(id, name, tier, kind, effect, color) {
    return { id: id, name: name, tier: tier, kind: kind, effect: effect, color: color, glow: glowOf(color) };
  }
  WOW.glowOf = glowOf;

  WOW.SKILLS = [
    // 일반 13종 (쿨타임 5초)
    s('S01', '연막',      'normal', 'projectile', '적중 시 상대 명중률 감소 및 시야 외곽 안개 부여', '#94a3b8'),
    s('S02', '독 장판',   'normal', 'area',       '구역 내 초당 지속 피해 누적', '#84cc16'),
    s('S03', '파이어볼',  'normal', 'area',       '소범위 집중 즉발 피해', '#f97316'),
    s('S04', '화살 세례', 'normal', 'area',       '광범위 다수 분산 피해', '#fbbf24'),
    s('S05', '토네이도',  'normal', 'projectile', '경로 상 적 유닛 넉백 및 피해', '#67e8f9'),
    s('S06', '번개',      'normal', 'area',       '지정 위치 타격 및 스플래시 피해', '#a78bfa'),
    s('S07', '총난사',    'normal', 'projectile', '열 전방 유닛부터 순차 연사 타격', '#ec4899'),
    s('S08', '방어 쉴드', 'normal', 'buff',       '피해 흡수 쉴드 생성 (피격 없을 시 HP 회복)', '#22d3ee'),
    s('S09', '타워 버프', 'normal', 'buff',       '본인 타워 방어력/공격력 일시 강화 (타워 파괴 시 비활성화)', '#c4b5fd'),
    s('S10', '집중',      'normal', 'buff',       '다음 공격 1회의 명중률 및 공격력 대폭 증가', '#facc15'),
    s('S11', '도끼 전사', 'normal', 'summon',     '근접 공격형 소환수 소환', '#dc2626'),
    s('S12', '마법사',    'normal', 'summon',     '원거리 공격형 소환수 소환', '#818cf8'),
    s('S13', '터렛',      'normal', 'install',    '적 유닛 요격용 고정 포탑 설치 (진영당 1기)', '#71717a'),
    // 고위력 6종 (쿨타임 20초, 게임당 3회)
    s('H01', '프리즈',    'high', 'global',     '적 전체 미니언 및 소환수 5초간 행동 정지', '#38bdf8'),
    s('H02', '레이저 빔', 'high', 'beam',       '0.5초 예고선 후 열 전체 관통 타격 (유닛 및 끝의 플레이어)', '#e879f9'),
    s('H03', '흡혈',      'high', 'projectile', '적 플레이어/유닛 타격 시 실 피해량 비례 HP 회복', '#be123c'),
    s('H04', '반사 결계', 'high', 'buff',       '상대 플레이어의 스킬 공격 1회 무효화 및 일부 피해 반사', '#2dd4bf'),
    s('H05', '분노',      'high', 'global',     '필드 내 기존 아군 유닛들의 공격력/공격속도 증가', '#ef4444'),
    s('H06', '소환탑',    'high', 'install',    '주기적으로 소형 근접 소환수를 생성하는 탑 설치', '#8b5cf6'),
  ];

  WOW.SKILL_MAP = {};
  WOW.SKILLS.forEach(function (k) { WOW.SKILL_MAP[k.id] = k; });

  WOW.getSkill = function (id) {
    const k = WOW.SKILL_MAP[id];
    if (!k) throw new Error('알 수 없는 스킬: ' + id);
    return k;
  };

  WOW.DECK_SIZE = 8;
  WOW.MAX_HIGH_IN_DECK = 2;

  /** 덱 검증. 문제가 없으면 null, 있으면 사유(한글) */
  WOW.validateDeck = function (ids) {
    ids = ids || [];
    if (ids.length !== WOW.DECK_SIZE) return '덱은 정확히 ' + WOW.DECK_SIZE + '장이어야 합니다. (현재 ' + ids.length + '장)';
    if (new Set(ids).size !== ids.length) return '같은 스킬을 중복해서 넣을 수 없습니다.';
    if (ids.some(function (id) { return !WOW.SKILL_MAP[id]; })) return '존재하지 않는 스킬이 포함되어 있습니다.';
    const high = ids.filter(function (id) { return WOW.SKILL_MAP[id].tier === 'high'; }).length;
    if (high > WOW.MAX_HIGH_IN_DECK) return '고위력 스킬은 덱에 최대 ' + WOW.MAX_HIGH_IN_DECK + '장까지 넣을 수 있습니다.';
    return null;
  };
})(window.WOW);
