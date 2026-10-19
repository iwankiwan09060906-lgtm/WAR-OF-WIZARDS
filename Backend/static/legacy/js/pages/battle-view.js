// 전투 HUD 화면 그리기 (battle.js 가 100ms 마다 view.render() 호출)
// S = 전투 상태(battleSim), ui = { stage, aim, hint, lastInput }, handlers = { onSlot, onLane, onCancelAim }
window.WOW = window.WOW || {};

WOW.battleView = function (S, ui, profile, settings, handlers) {
  const $ = WOW.$;
  const esc = WOW.esc;
  const R = WOW.RULES;
  const sim = WOW.sim;
  const STAGES = ['Drawing', 'Recognizing', 'RuneReady', 'Armed', 'Aiming', 'Cast'];
  const TIER_TEXT = [
    { label: '방어 중', desc: '타워·넥서스 생존 — 받는 피해 20% · HP 회복', color: '#22d3ee' },
    { label: '타워 파괴', desc: '받는 피해 100% · HP 회복', color: '#fbbf24' },
    { label: '돌파당함', desc: '받는 피해 100% · HP 회복 중단 · 유닛이 직접 공격', color: '#ef4444' },
  ];

  // ── 고정 요소는 한 번만 만든다 (매 프레임 다시 만들면 클릭이 끊김) ─────
  const slotEls = S.deck.map(function (id, i) {
    const k = WOW.SKILL_MAP[id];
    const b = document.createElement('button');
    b.className = 'slot';
    b.setAttribute('aria-label', (i + 1) + '번 ' + k.name);
    b.innerHTML = '<span class="slot-no">' + (i + 1) + '</span><div class="slot-icon"></div>' +
      '<span class="slot-name display">' + esc(k.name) + '</span><div class="uses"></div>' +
      '<div class="cd-cover hidden"><span class="display"></span></div><div class="spent hidden display">소진</div>';
    b.onclick = function () { handlers.onSlot(i); };
    $('#slots').appendChild(b);
    return b;
  });

  const laneEls = [0, 1, 2].map(function (c) {
    const d = document.createElement('div');
    d.className = 'lane';
    d.innerHTML = '<div class="lane-label display">' + WOW.COLUMN_LABEL[c] + '</div><div class="lane-body"></div>' +
      '<div class="lane-fx"></div><span class="aim-tag hidden">' + ['Q', 'W', 'E'][c] + ' · 조준</span>';
    d.onclick = function () { handlers.onLane(c); };
    $('#lanes').appendChild(d);
    return d;
  });

  $('#stages').innerHTML = STAGES.map(function (s) {
    return '<div class="stage-row" data-stage="' + s + '"><span class="stage-dot"></span>' + s + '</div>';
  }).join('');

  function chip(c) {
    return '<span class="chip" style="color:' + c.color + ';border-color:' + c.color + '66;background:' + c.color + '1a">' +
      esc(c.label) + (c.sec !== undefined ? ' ' + Math.ceil(c.sec) + 's' : '') + '</span>';
  }

  function statusChips(sd, isMe) {
    const out = [];
    if (isMe) {
      if (sd.focus) out.push({ label: '집중', color: '#facc15' });
      if (sd.shield > 0) out.push({ label: '쉴드 ' + Math.ceil(sd.shield), sec: sd.shieldT, color: '#22d3ee' });
      if (sd.reflectT > 0) out.push({ label: '반사 결계', sec: sd.reflectT, color: '#2dd4bf' });
      if (sd.rageT > 0) out.push({ label: '분노', sec: sd.rageT, color: '#ef4444' });
      if (sd.towerBuffT > 0) out.push({ label: '타워 버프', sec: sd.towerBuffT, color: '#c4b5fd' });
      if (sd.turretT > 0) out.push({ label: '터렛', sec: sd.turretT, color: '#94a3b8' });
      if (sd.summonTowerT > 0) out.push({ label: '소환탑', sec: sd.summonTowerT, color: '#8b5cf6' });
    }
    if (sd.smokeT > 0) out.push({ label: '연막 (명중률↓)', sec: sd.smokeT, color: '#94a3b8' });
    if (sd.frozenT > 0) out.push({ label: '프리즈', sec: sd.frozenT, color: '#38bdf8' });
    return out;
  }

  function tierTag(el, tier) {
    const t = TIER_TEXT[tier];
    el.textContent = t.label; el.style.color = t.color; el.style.background = t.color + '22';
  }

  const gap = '<div style="height:8px"></div>';

  function renderTop() {
    const idle = (Date.now() - ui.lastInput) / 1000;
    $('#t-time').textContent = WOW.fmtTime(S.t);
    $('#t-wave').textContent = S.wave;
    $('#t-phase-cap').textContent = '강화 ' + S.phase + '단계';
    $('#t-phase').textContent = '다음 ' + WOW.fmtTime(R.phase.interval - (S.t % R.phase.interval));
    const afk = $('#afk');
    afk.classList.toggle('hidden', !(idle >= R.afk.warnAfter && S.status === 'run'));
    afk.textContent = '⚠ 입력이 없습니다 — ' + Math.ceil(Math.max(0, R.afk.loseAfter - idle)) + '초 후 기권패';
  }

  function renderSides() {
    const M = S.me, F = S.foe;
    const myTier = sim.defenseTier(M), foeTier = sim.defenseTier(F);

    tierTag($('#my-tier'), myTier);
    $('#my-bars').innerHTML = WOW.bar(Math.ceil(M.tower), R.structure.tower, 'cyan', { label: '타워', showValue: true, height: 8 }) + gap +
      WOW.bar(Math.ceil(M.nexus), R.structure.nexus, 'purple', { label: '넥서스', showValue: true, height: 8 });
    $('#my-tier-desc').textContent = TIER_TEXT[myTier].desc;
    const atk = M.permMul * (1 + S.phase * R.phase.atkBoost);
    $('#my-atk').textContent = '공격 배율 ×' + atk.toFixed(2) + (M.permMul > 1 ? ' (진영 보너스 +' + Math.round((M.permMul - 1) * 100) + '%)' : '');

    tierTag($('#foe-tier'), foeTier);
    $('#foe-bars').innerHTML = WOW.bar(Math.ceil(F.hp), F.maxHp, 'red', { label: 'HP', showValue: true, height: 10 }) + gap +
      WOW.bar(Math.ceil(F.tower), R.structure.tower, 'orange', { label: '적 타워', showValue: true, height: 7 }) + gap +
      WOW.bar(Math.ceil(F.nexus), R.structure.nexus, 'purple', { label: '적 넥서스', showValue: true, height: 7 });
    const ratio = sim.takeRatio(F);
    $('#foe-ratio').textContent = '내 스킬 피해 배율 ×' + ratio.toFixed(1) + (ratio < 1 ? ' (타워 락)' : '');
    $('#foe-chips').innerHTML = statusChips(F, false).map(chip).join('');

    $('#my-hp').innerHTML = WOW.bar(Math.ceil(M.hp), M.maxHp, 'purple', { label: profile.nickname, showValue: true, height: 14 });
    $('#my-regen').textContent = myTier < 2 ? '다음 HP 회복 ' + Math.ceil(R.defense.regenInterval - M.regenT) + '초' : 'HP 회복 중단 (돌파당함)';
    const myChips = statusChips(M, true);
    $('#my-chips').innerHTML = myChips.length ? myChips.map(chip).join('') : '<span class="hud-small" style="color:var(--purple-700)">상태 효과 없음</span>';
  }

  function renderField() {
    const M = S.me, F = S.foe;
    $('#unit-bars').innerHTML =
      WOW.bar(Math.round(M.minions), R.wave.unitCap, 'cyan', { label: '아군 유닛 ' + Math.round(M.minions), height: 8 }) +
      WOW.bar(Math.round(F.minions), R.wave.unitCap, 'red', { label: '적 유닛 ' + Math.round(F.minions), height: 8 });
    laneEls.forEach(function (el, c) {
      const zone = S.zones.some(function (z) { return z.col === c; });
      const beam = S.beams.some(function (b) { return b.col === c; });
      el.classList.toggle('aiming', !!ui.aim);
      el.classList.toggle('zone', zone);
      el.classList.toggle('beam', beam);
      $('.lane-body', el).innerHTML =
        (F.col === c ? '<div class="unit foe' + (settings.colorblind ? ' diamond' : '') + '">적</div>' : '') +
        (M.col === c ? '<div class="unit me">나</div>' : '');
      $('.lane-fx', el).innerHTML = (zone ? '<span style="color:#bef264">☣ 독 장판</span>' : '') +
        (beam ? '<span class="blink" style="color:#f0abfc">⚠ 레이저 예고</span>' : '');
      $('.aim-tag', el).classList.toggle('hidden', !ui.aim);
    });
    $('#my-col').textContent = WOW.COLUMN_LABEL[M.col];
  }

  function renderLog() {
    if (settings.subtitles) {
      const last = S.log[S.log.length - 1];
      $('#log').innerHTML = '<div class="subtitle">' + esc(last ? last.text : '') + '</div>';
      return;
    }
    const logs = S.log.slice(-3);
    $('#log').innerHTML = logs.map(function (l, i) {
      const color = l.tone === 'me' ? '#67e8f9' : l.tone === 'foe' ? '#fca5a5' : '#c4b5fd';
      return '<div style="opacity:' + (0.45 + (i / logs.length) * 0.55) + ';color:' + color + '">' + esc(l.text) + '</div>';
    }).join('');
  }

  function renderSlots() {
    slotEls.forEach(function (el, i) {
      const k = WOW.SKILL_MAP[S.deck[i]];
      const cd = S.cd[i], uses = S.uses[i];
      const spent = uses <= 0, ready = cd <= 0 && !spent, active = ui.aim && ui.aim.slot === i;
      el.style.borderColor = active ? '#22d3ee' : ready ? k.color + '88' : 'rgba(124,58,237,0.25)';
      el.style.boxShadow = active ? '0 0 18px rgba(34,211,238,0.7)' : ready ? '0 0 12px ' + WOW.glowOf(k.color, 0.3) : 'none';
      el.style.filter = spent ? 'grayscale(1)' : 'none';
      el.style.cursor = ready ? 'pointer' : 'not-allowed';
      const iconBox = $('.slot-icon', el);
      if (iconBox.getAttribute('data-ready') !== String(ready)) {
        iconBox.innerHTML = WOW.skillIcon(k.id, 52, { active: ready });
        iconBox.setAttribute('data-ready', String(ready));
      }
      $('.slot-name', el).style.color = ready ? k.color : '#6b6a8a';
      $('.uses', el).innerHTML = Number.isFinite(uses) ? new Array(R.highUsesPerGame).fill(0).map(function (_, n) {
        return '<span style="background:' + (n < uses ? '#fbbf24' : 'rgba(255,255,255,0.12)') + '"></span>';
      }).join('') : '';
      const cover = $('.cd-cover', el);
      cover.classList.toggle('hidden', !(cd > 0));
      cover.style.height = Math.min(100, (cd / S.cdMax[i]) * 100) + '%';
      $('span', cover).textContent = Math.ceil(cd);
      $('.spent', el).classList.toggle('hidden', !spent);
    });
  }

  function renderCastState() {
    WOW.$$('.stage-row').forEach(function (r) { r.classList.toggle('on', r.getAttribute('data-stage') === ui.stage); });
    const hintEl = $('#cast-hint');
    if (ui.aim) {
      const left = Math.max(0, R.aimTimeout - (Date.now() - ui.aim.at) / 1000);
      hintEl.style.color = '#67e8f9';
      hintEl.innerHTML = '조준할 열을 선택하세요 (Q/W/E · 클릭) · ' + left.toFixed(1) + '초 <button class="link" id="btn-cancel-aim" style="text-decoration:underline">취소</button>';
      $('#btn-cancel-aim').onclick = handlers.onCancelAim;
    } else {
      hintEl.style.color = '#fbbf24';
      hintEl.textContent = ui.hint || '슬롯 클릭 또는 1~8 키로 시전';
    }
  }

  return {
    render: function () {
      renderTop();
      renderSides();
      renderField();
      renderLog();
      renderSlots();
      renderCastState();
    },
  };
};
