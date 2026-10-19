/* ==========================================================================
   전투 시뮬레이터 (로컬 HUD 프로토타입용)
   실제 판정은 Photon 전용 서버가 담당하고, 이 파일은 HUD 가 명세서대로 움직이는지 보여주는 로컬 시뮬레이션.
   화면과 분리된 순수 로직: battle.html 이 WOW.sim.tick() / WOW.sim.cast() 를 호출한다.

   명세서 반영 항목
    · 플레이어 스킬은 구조물(타워/넥서스) 타격 불가, 상대 플레이어·유닛만 타격
    · 방어 계층: 타워 생존 20% / 타워 파괴 100% / 넥서스까지 파괴(돌파) 100% + HP 회복 중단
    · 진영 배율: 적 타워 파괴 +20%, 적 넥서스 파괴 +40% (영구)
    · 웨이브 10초 주기 (근접2+원거리1), 3회차마다 Brute, 진영당 최대 20기
    · 쿨타임 일반 5초 / 고위력 20초, 고위력 게임당 3회
    · 5분마다 양측 강화 단계, HP 0 즉시 패배

   ▶ 새 스킬 효과 추가: data.js 에 스킬을 넣고 아래 cast() 의 switch 에 case 'S14': ... 추가
   ========================================================================== */
window.WOW = window.WOW || {};

(function (WOW) {
const RULES = WOW.RULES;
const SKILL_MAP = WOW.SKILL_MAP;
const KIND_META = WOW.KIND_META;

const STRUCT_DPS = 0.25; // [TBD] 우세 유닛 1기당 초당 구조물 피해
const CLASH_RATE = 0.08; // [TBD] 유닛 간 교전 처치율
const BREAKTHROUGH_DPS = 0.15; // [TBD] 돌파 후 유닛이 플레이어에게 주는 초당 피해
const DAMAGE_SKILLS = new Set(['S02', 'S03', 'S04', 'S05', 'S06', 'S07', 'H02', 'H03']);

function newSide() {
  return {
    hp: RULES.player.maxHp, maxHp: RULES.player.maxHp,
    tower: RULES.structure.tower, nexus: RULES.structure.nexus,
    minions: 3, col: 1,
    shield: 0, shieldT: 0, shieldTouched: false, reflectT: 0, focus: false,
    frozenT: 0, rageT: 0, towerBuffT: 0, turretT: 0, summonTowerT: 0, summonAcc: 0, smokeT: 0,
    regenT: 0, permMul: 1,
  };
}

function createBattle(deck, rng = Math.random) {
  return {
    t: 0, wave: 0, nextWaveAt: RULES.wave.interval, phase: 0,
    me: newSide(), foe: newSide(),
    deck: [...deck],
    cd: deck.map(() => 0),
    cdMax: deck.map(id => baseCooldown(id)),
    uses: deck.map(id => (SKILL_MAP[id]?.tier === 'high' ? RULES.highUsesPerGame : Infinity)),
    zones: [], beams: [],
    botT: 5, botMoveT: 6,
    status: 'run', endReason: '',
    log: [{ t: 0, text: '전투 시작! 상대 넥서스를 파괴해 돌파하세요.', tone: 'sys' }],
    stats: {
      casts: 0, damageDealt: 0, damageTaken: 0, minionsKilled: 0,
      towerDestroyed: false, nexusDestroyed: false, ownTowerLost: false, ownNexusLost: false,
    },
    rng,
  };
}

// ── 헬퍼 ─────────────────────────────────────────────────────────────────

function baseCooldown(skillId) {
  return SKILL_MAP[skillId]?.tier === 'high' ? RULES.cooldown.high : RULES.cooldown.normal;
}

function cooldownFor(skillId, phase) {
  const cut = Math.min(0.5, phase * RULES.phase.cooldownCut);
  return baseCooldown(skillId) * (1 - cut);
}
/** 0: 타워·넥서스 생존 / 1: 타워 파괴 / 2: 타워·넥서스 모두 파괴(돌파) */

function defenseTier(sd) {
  if (sd.tower > 0)
    return 0;
  return sd.nexus > 0 ? 1 : 2;
}

function takeRatio(sd) {
  return defenseTier(sd) === 0 ? RULES.defense.towerAliveTakeRatio : 1;
}

const other = (s) => (s === 'me' ? 'foe' : 'me');
const atkMul = (s, side) => s[side].permMul * (1 + s.phase * RULES.phase.atkBoost);

function say(s, text, tone) {
  s.log.push({ t: s.t, text, tone });
  if (s.log.length > 40)
    s.log.shift();
}

function addMinions(sd, n) {
  sd.minions = Math.min(RULES.wave.unitCap, sd.minions + n);
}

function killMinions(s, side, n) {
  const sd = s[side];
  const k = Math.min(sd.minions, n);
  sd.minions -= k;
  if (side === 'foe')
    s.stats.minionsKilled += k;
  return k;
}

function checkEnd(s) {
  if (s.status !== 'run')
    return;
  if (s.me.hp <= 0) {
    s.status = 'lose';
    s.endReason = 'HP가 0이 되어 패배했습니다.';
  }
  else if (s.foe.hp <= 0) {
    s.status = 'win';
    s.endReason = '상대 플레이어를 처치했습니다.';
  }
  if (s.status !== 'run')
    say(s, s.endReason, 'sys');
}

function forceEnd(s, result, reason) {
  if (s.status !== 'run')
    return;
  s.status = result;
  s.endReason = reason;
  say(s, reason, 'sys');
}
/** 플레이어 스킬 → 상대 플레이어. 방어 계층(20%/100%), 명중, 반사, 쉴드 순서로 처리(명세서 §3.2). */

function hitPlayer(s, attacker, base, opts = {}) {
  const A = s[attacker];
  const target = other(attacker);
  const T = s[target];
  if (!opts.sure && A.smokeT > 0 && s.rng() < 0.4) {
    say(s, attacker === 'me' ? '공격이 연막 때문에 빗나갔습니다!' : 'Bot 공격이 연막 때문에 빗나갔습니다!', attacker);
    return 0;
  }
  if (T.reflectT > 0) {
    T.reflectT = 0;
    const back = base * 0.3 * atkMul(s, target);
    A.hp = Math.max(0, A.hp - back);
    say(s, `반사 결계 발동! 공격이 무효화되고 ${Math.round(back)} 피해가 반사됩니다.`, target);
    checkEnd(s);
    return 0;
  }
  let dmg = base * atkMul(s, attacker) * (opts.mul ?? 1) * takeRatio(T);
  if (T.shield > 0) {
    const absorbed = Math.min(T.shield, dmg);
    T.shield -= absorbed;
    T.shieldTouched = true;
    dmg -= absorbed;
  }
  T.hp = Math.max(0, T.hp - dmg);
  if (attacker === 'me')
    s.stats.damageDealt += dmg;
  else
    s.stats.damageTaken += dmg;
  checkEnd(s);
  return dmg;
}
/** 지속 피해(독 장판 등): 명중/반사 판정 없이 방어 계층·쉴드만 적용 */

function dotPlayer(s, attacker, dps, dt, mul) {
  const T = s[other(attacker)];
  let dmg = dps * dt * atkMul(s, attacker) * mul * takeRatio(T);
  if (T.shield > 0) {
    const a = Math.min(T.shield, dmg);
    T.shield -= a;
    T.shieldTouched = true;
    dmg -= a;
  }
  T.hp = Math.max(0, T.hp - dmg);
  if (attacker === 'me')
    s.stats.damageDealt += dmg;
  else
    s.stats.damageTaken += dmg;
  checkEnd(s);
}

function onTowerDown(s, owner) {
  const sd = s[owner];
  sd.towerBuffT = 0;
  s[other(owner)].permMul += RULES.permBuff.towerBreak;
  if (owner === 'foe') {
    s.stats.towerDestroyed = true;
    say(s, `적 타워 파괴! 공격력 영구 +${RULES.permBuff.towerBreak * 100}%`, 'me');
  }
  else {
    s.stats.ownTowerLost = true;
    say(s, `아군 타워 파괴! 상대 공격력 영구 +${RULES.permBuff.towerBreak * 100}%, 받는 피해가 100%가 됩니다.`, 'foe');
  }
}

function onNexusDown(s, owner) {
  s[other(owner)].permMul += RULES.permBuff.nexusBreak;
  if (owner === 'foe') {
    s.stats.nexusDestroyed = true;
    say(s, `적 넥서스 파괴! 돌파 상태 — 공격력 영구 +${RULES.permBuff.nexusBreak * 100}%`, 'me');
  }
  else {
    s.stats.ownNexusLost = true;
    say(s, '아군 넥서스 파괴! 돌파당했습니다. HP 회복이 중단되고 상대 유닛이 직접 공격합니다.', 'foe');
  }
}
/** 유닛이 owner 진영 구조물을 공격. 타워 → 넥서스 순, 둘 다 파괴되면 플레이어를 직접 공격(돌파). */

function pushStructure(s, owner, amount, surplus, dt) {
  const sd = s[owner];
  let a = amount;
  if (sd.tower > 0) {
    if (sd.towerBuffT > 0)
      a *= 0.5;
    const d = Math.min(sd.tower, a);
    sd.tower -= d;
    a -= d;
    if (sd.tower <= 0)
      onTowerDown(s, owner);
    if (sd.tower > 0)
      return;
  }
  if (sd.nexus > 0) {
    const d = Math.min(sd.nexus, a);
    sd.nexus -= d;
    if (sd.nexus <= 0)
      onNexusDown(s, owner);
    return;
  }
  // 돌파 상태: 상대 유닛이 플레이어를 직접 공격 (회피 불가)
  const dmg = surplus * BREAKTHROUGH_DPS * dt;
  sd.hp = Math.max(0, sd.hp - dmg);
  if (owner === 'me')
    s.stats.damageTaken += dmg;
  else
    s.stats.damageDealt += dmg;
  checkEnd(s);
}

// ── 시간 진행 ────────────────────────────────────────────────────────────

function step(s, dt) {
  s.t += dt;
  const M = s.me;
  const F = s.foe;
  // 강화 단계 (5분마다, 양측 공통)
  const phase = Math.floor(s.t / RULES.phase.interval);
  if (phase > s.phase) {
    s.phase = phase;
    say(s, `강화 단계 ${phase}! 양측 쿨타임 -${Math.round(phase * RULES.phase.cooldownCut * 100)}%, 공격력 +${Math.round(phase * RULES.phase.atkBoost * 100)}%`, 'sys');
  }
  // 웨이브 (10초 주기, 3회차마다 Brute)
  while (s.t >= s.nextWaveAt) {
    s.wave++;
    s.nextWaveAt += RULES.wave.interval;
    const brute = s.wave % RULES.wave.bruteEvery === 0;
    const n = RULES.wave.melee + RULES.wave.ranged + (brute ? RULES.wave.brute * 2 : 0); // Brute 는 2기 분량으로 환산
    addMinions(M, n);
    addMinions(F, n);
    say(s, brute ? `웨이브 ${s.wave} — 대형 유닛(Brute) 등장!` : `웨이브 ${s.wave} 스폰`, 'sys');
  }
  // 쿨타임
  for (let i = 0; i < s.cd.length; i++)
    if (s.cd[i] > 0)
      s.cd[i] = Math.max(0, s.cd[i] - dt);
  // 버프/상태 타이머
  for (const sd of [M, F]) {
    sd.frozenT = Math.max(0, sd.frozenT - dt);
    sd.rageT = Math.max(0, sd.rageT - dt);
    sd.towerBuffT = Math.max(0, sd.towerBuffT - dt);
    sd.turretT = Math.max(0, sd.turretT - dt);
    sd.smokeT = Math.max(0, sd.smokeT - dt);
    sd.reflectT = Math.max(0, sd.reflectT - dt);
    if (sd.shieldT > 0) {
      sd.shieldT -= dt;
      if (sd.shieldT <= 0) {
        if (sd.shield > 0 && !sd.shieldTouched) {
          sd.hp = Math.min(sd.maxHp, sd.hp + 5);
          if (sd === M)
            say(s, '쉴드가 피격 없이 만료되어 HP를 회복했습니다.', 'me');
        }
        sd.shield = 0;
      }
    }
    if (sd.summonTowerT > 0) {
      sd.summonTowerT = Math.max(0, sd.summonTowerT - dt);
      sd.summonAcc += dt;
      while (sd.summonAcc >= 3) {
        sd.summonAcc -= 3;
        addMinions(sd, 1);
      }
    }
    // HP 회복 (돌파 상태에서는 중단)
    sd.regenT += dt;
    if (sd.regenT >= RULES.defense.regenInterval) {
      sd.regenT = 0;
      if (defenseTier(sd) < 2)
        sd.hp = Math.min(sd.maxHp, sd.hp + RULES.defense.regenAmount);
    }
  }
  // 독 장판
  for (const z of s.zones) {
    z.remain -= dt;
    if (F.col === z.col)
      dotPlayer(s, 'me', 3, dt, z.mul);
    killMinions(s, 'foe', 0.6 * dt);
  }
  s.zones = s.zones.filter(z => z.remain > 0);
  // 예고선 후 발동하는 레이저 빔
  for (const b of s.beams) {
    if (s.t < b.at)
      continue;
    killMinions(s, 'foe', 5);
    if (F.col === b.col) {
      const d = hitPlayer(s, 'me', 30, { mul: b.mul, sure: true });
      say(s, `레이저 빔 적중! ${Math.round(d)} 피해`, 'me');
    }
    else
      say(s, '레이저 빔이 빗나갔습니다. (상대가 열을 이동함)', 'me');
    b.at = Infinity;
  }
  s.beams = s.beams.filter(b => b.at !== Infinity);
  // 유닛 교전
  const mAct = M.frozenT > 0 ? 0 : 1 + (M.rageT > 0 ? 0.5 : 0);
  const fAct = F.frozenT > 0 ? 0 : 1 + (F.rageT > 0 ? 0.5 : 0);
  const meKills = Math.min(F.minions, M.minions * CLASH_RATE * mAct * dt);
  const foeKills = Math.min(M.minions, F.minions * CLASH_RATE * fAct * dt);
  F.minions -= meKills;
  M.minions -= foeKills;
  s.stats.minionsKilled += meKills;
  // 타워·터렛 자동 방어 (유닛만 공격, 플레이어 공격 불가)
  const defRate = (sd) => (sd.tower > 0 ? 0.15 : 0) + (sd.turretT > 0 ? 0.4 : 0) + (sd.towerBuffT > 0 && sd.tower > 0 ? 0.25 : 0);
  const byMe = Math.min(F.minions, defRate(M) * dt);
  const byFoe = Math.min(M.minions, defRate(F) * dt);
  F.minions -= byMe;
  M.minions -= byFoe;
  s.stats.minionsKilled += byMe;
  // 우세 유닛이 상대 구조물 공격
  const surMe = Math.max(0, M.minions - F.minions) * mAct;
  const surFoe = Math.max(0, F.minions - M.minions) * fAct;
  if (surMe > 0)
    pushStructure(s, 'foe', surMe * STRUCT_DPS * dt, surMe, dt);
  if (surFoe > 0)
    pushStructure(s, 'me', surFoe * STRUCT_DPS * dt, surFoe, dt);
  // 상대 봇
  s.botMoveT -= dt;
  if (s.botMoveT <= 0) {
    s.botMoveT = 5 + s.rng() * 8;
    F.col = Math.floor(s.rng() * 3);
  }
  s.botT -= dt;
  if (s.botT <= 0 && s.status === 'run') {
    s.botT = 4 + s.rng() * 3;
    const r = s.rng();
    if (r < 0.4) {
      const aimCol = (s.rng() < 0.6 ? M.col : Math.floor(s.rng() * 3));
      killMinions(s, 'me', 1);
      if (aimCol === M.col) {
        const d = hitPlayer(s, 'foe', 8);
        if (d > 0)
          say(s, `Bot 스킬 적중 (${Math.round(d)} 피해)`, 'foe');
      }
      else
        say(s, 'Bot 스킬이 빗나갔습니다.', 'foe');
    }
    else if (r < 0.7) {
      const k = killMinions(s, 'me', 2);
      if (k > 0)
        say(s, 'Bot 광역 스킬로 아군 유닛을 처치했습니다.', 'foe');
    }
    else {
      addMinions(F, 3);
      say(s, 'Bot이 소환수를 추가했습니다.', 'foe');
    }
  }
  checkEnd(s);
}

function tick(s, dt) {
  let left = dt;
  while (left > 1e-9 && s.status === 'run') {
    const d = Math.min(0.1, left);
    step(s, d);
    left -= d;
  }
}

// ── 플레이어 입력 ────────────────────────────────────────────────────────

function moveMe(s, dir) {
  const next = Math.min(2, Math.max(0, s.me.col + dir));
  s.me.col = next;
}

function cast(s, slot, col) {
  if (s.status !== 'run')
    return { ok: false, reason: '전투가 종료되었습니다.' };
  const id = s.deck[slot];
  const k = id ? SKILL_MAP[id] : undefined;
  if (!k)
    return { ok: false, reason: '비어 있는 슬롯입니다.' };
  if (s.cd[slot] > 0)
    return { ok: false, reason: '쿨타임 중입니다.' };
  if (s.uses[slot] <= 0)
    return { ok: false, reason: '이번 게임의 시전 횟수를 모두 사용했습니다.' };
  if (KIND_META[k.kind].needsAim && col == null)
    return { ok: false, reason: '조준할 열을 선택해주세요.' };
  const M = s.me;
  const F = s.foe;
  const isDamage = DAMAGE_SKILLS.has(id);
  const focused = M.focus && isDamage;
  const mul = focused ? 2 : 1;
  const c = (col ?? 1);
  const colName = ['좌', '중', '우'][c];
  // 사전 조건 검사 (실패 시 쿨타임/횟수 소모 없음)
  if (id === 'S09' && M.tower <= 0)
    return { ok: false, reason: '타워가 파괴되어 사용할 수 없습니다.' };
  if (id === 'S13' && M.turretT > 0)
    return { ok: false, reason: '터렛은 진영당 1기만 설치할 수 있습니다.' };
  if (id === 'H06' && M.summonTowerT > 0)
    return { ok: false, reason: '소환탑이 이미 작동 중입니다.' };
  switch (id) {
    case 'S01':
      killMinions(s, 'foe', 1);
      if (F.col === c) {
        F.smokeT = 8;
        say(s, '연막 적중! 상대 명중률이 8초간 감소합니다.', 'me');
      }
      else
        say(s, `연막(${colName})이 빗나갔습니다.`, 'me');
      break;
    case 'S02':
      s.zones.push({ col: c, remain: 6, mul });
      say(s, `독 장판 설치 (${colName})`, 'me');
      break;
    case 'S03': {
      killMinions(s, 'foe', 2);
      if (F.col === c) {
        const d = hitPlayer(s, 'me', 14, { mul, sure: focused });
        say(s, `파이어볼 적중 ${Math.round(d)} 피해`, 'me');
      }
      else
        say(s, `파이어볼(${colName}) — 유닛만 처치`, 'me');
      break;
    }
    case 'S04': {
      killMinions(s, 'foe', 3);
      if (Math.abs(F.col - c) <= 1) {
        const d = hitPlayer(s, 'me', 8, { mul, sure: focused });
        say(s, `화살 세례 적중 ${Math.round(d)} 피해`, 'me');
      }
      else
        say(s, `화살 세례(${colName}) — 유닛만 처치`, 'me');
      break;
    }
    case 'S05': {
      killMinions(s, 'foe', 2);
      if (F.col === c) {
        const d = hitPlayer(s, 'me', 6, { mul, sure: focused });
        F.col = Math.floor(s.rng() * 3);
        say(s, `토네이도 적중 ${Math.round(d)} 피해 + 넉백`, 'me');
      }
      else
        say(s, `토네이도(${colName}) — 유닛 넉백`, 'me');
      break;
    }
    case 'S06': {
      killMinions(s, 'foe', 2);
      if (F.col === c) {
        const d = hitPlayer(s, 'me', 16, { mul, sure: focused });
        say(s, `번개 적중 ${Math.round(d)} 피해`, 'me');
      }
      else
        say(s, `번개(${colName}) — 유닛만 처치`, 'me');
      break;
    }
    case 'S07': {
      killMinions(s, 'foe', 3);
      if (F.col === c) {
        const d = hitPlayer(s, 'me', 12, { mul, sure: focused });
        say(s, `총난사 적중 ${Math.round(d)} 피해`, 'me');
      }
      else
        say(s, `총난사(${colName}) — 유닛만 처치`, 'me');
      break;
    }
    case 'S08':
      M.shield = 20;
      M.shieldT = 8;
      M.shieldTouched = false;
      say(s, '방어 쉴드 생성 (20 흡수 / 8초)', 'me');
      break;
    case 'S09':
      M.towerBuffT = 10;
      say(s, '타워 버프! 10초간 타워 강화', 'me');
      break;
    case 'S10':
      M.focus = true;
      say(s, '집중! 다음 공격 스킬이 필중 + 피해 2배', 'me');
      break;
    case 'S11':
      addMinions(M, 3);
      say(s, `도끼 전사 소환 (${colName} 라인)`, 'me');
      break;
    case 'S12':
      addMinions(M, 3);
      say(s, `마법사 소환 (${colName} 라인)`, 'me');
      break;
    case 'S13':
      M.turretT = 30;
      say(s, `터렛 설치 (${colName}) — 30초간 유닛 요격`, 'me');
      break;
    case 'H01':
      F.frozenT = 5;
      say(s, '프리즈! 적 유닛이 5초간 행동 정지', 'me');
      break;
    case 'H02':
      s.beams.push({ at: s.t + 0.5, col: c, mul });
      say(s, `레이저 빔 예고선 (${colName}) — 0.5초 후 발사`, 'me');
      break;
    case 'H03': {
      killMinions(s, 'foe', 2);
      let healed = 2;
      if (F.col === c) {
        const d = hitPlayer(s, 'me', 12, { mul, sure: focused });
        healed += d;
        say(s, `흡혈 적중 ${Math.round(d)} 피해`, 'me');
      }
      else
        say(s, `흡혈(${colName}) — 유닛만 처치`, 'me');
      M.hp = Math.min(M.maxHp, M.hp + healed);
      break;
    }
    case 'H04':
      M.reflectT = 15;
      say(s, '반사 결계 전개 — 다음 스킬 공격 1회를 무효화합니다.', 'me');
      break;
    case 'H05':
      M.rageT = 10;
      say(s, '분노! 아군 유닛 공격력/공격속도 증가 (10초)', 'me');
      break;
    case 'H06':
      M.summonTowerT = 30;
      M.summonAcc = 0;
      say(s, `소환탑 설치 (${colName}) — 30초간 소환수 생성`, 'me');
      break;
  }
  if (focused)
    M.focus = false;
  s.cd[slot] = cooldownFor(id, s.phase);
  s.cdMax[slot] = s.cd[slot];
  if (Number.isFinite(s.uses[slot]))
    s.uses[slot]--;
  s.stats.casts++;
  checkEnd(s);
  return { ok: true };
}

WOW.sim = { createBattle, baseCooldown, cooldownFor, defenseTier, takeRatio, forceEnd, tick, moveMe, cast };
})(window.WOW);
