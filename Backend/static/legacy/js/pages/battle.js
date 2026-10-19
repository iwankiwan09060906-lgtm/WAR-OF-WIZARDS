// 캔버스 12 · 인게임 전투 HUD
// 전투 로직은 js/battleSim.js (WOW.sim), 화면 그리기는 js/pages/battle-view.js (WOW.battleView).
// 이 파일은 입력 처리 + 메인 루프 + 결과 저장을 담당한다.
WOW.page({ auth: true, deck: true }, function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const R = WOW.RULES;
  const sim = WOW.sim;
  const settings = WOW.settings.get();
  const FALLBACK_DECK = ['S03', 'S02', 'S07', 'S08', 'S11', 'S13', 'H01', 'H02'];

  let match = WOW.state.getMatch();
  if (!match) { WOW.beginMatch('bot', { name: 'Bot', rating: ctx.profile.rating, isBot: true }); match = WOW.state.getMatch(); }

  const deckValid = WOW.validateDeck(ctx.deck) === null;
  const S = sim.createBattle(deckValid ? ctx.deck : FALLBACK_DECK);
  $('#deck-note').classList.toggle('hidden', deckValid);
  $('#foe-name').textContent = match.opponent.name;
  $('#vignette').classList.toggle('hidden', !settings.comfortVignette);

  // 화면과 입력이 함께 쓰는 상태
  const ui = {
    stage: 'Idle',     // 시전 상태 머신 단계
    aim: null,         // 조준 중이면 { slot, at }
    hint: '',
    busy: false,
    lastInput: Date.now(),
  };
  let speed = 1;
  let finished = false;

  const touch = function () { ui.lastInput = Date.now(); };
  const flash = function (msg) { ui.hint = msg; setTimeout(function () { if (ui.hint === msg) ui.hint = ''; }, 2200); };

  // ── 시전 흐름: 슬롯 선택 → (룬 드로잉 연출) → 조준 → 시전 ─────────────
  function finishCast(slot, col) {
    const r = sim.cast(S, slot, col);
    ui.aim = null;
    if (r.ok) {
      ui.stage = 'Cast';
      setTimeout(function () { ui.stage = 'Idle'; ui.busy = false; }, 450);
    } else {
      flash(r.reason);
      ui.stage = 'Idle';
      ui.busy = false;
    }
    view.render();
  }

  function cancelAim(why) {
    ui.aim = null; ui.stage = 'Idle'; ui.busy = false;
    flash(why);
    view.render();
  }

  function startCast(slot) {
    touch();
    if (S.status !== 'run' || ui.busy) return;
    const k = WOW.SKILL_MAP[S.deck[slot]];
    if (!k) return;
    if (S.cd[slot] > 0) { flash('쿨타임 중입니다.'); return; }
    if (S.uses[slot] <= 0) { flash('이번 게임의 시전 횟수를 모두 사용했습니다.'); return; }
    ui.busy = true;
    // 프로토타입: 실제로는 룬 드로잉(핀치) → 인식 → 주먹 → 포인팅. 여기서는 클릭 한 번으로 상태 흐름만 재현.
    [['Drawing', 0], ['Recognizing', 120], ['RuneReady', 260], ['Armed', 400]].forEach(function (p) {
      setTimeout(function () { ui.stage = p[0]; }, p[1]);
    });
    setTimeout(function () {
      if (WOW.KIND_META[k.kind].needsAim) { ui.stage = 'Aiming'; ui.aim = { slot: slot, at: Date.now() }; view.render(); }
      else finishCast(slot, null);
    }, 560);
  }

  const view = WOW.battleView(S, ui, ctx.profile, settings, {
    onSlot: startCast,
    onLane: function (c) { if (ui.aim) { touch(); finishCast(ui.aim.slot, c); } },
    onCancelAim: function () { cancelAim('조준을 취소했습니다. (쿨타임 소모 없음)'); },
  });

  // ── 메인 루프 (100ms) ─────────────────────────────────────────────
  let last = performance.now();
  const loop = setInterval(function () {
    const now = performance.now();
    const real = Math.min(0.5, (now - last) / 1000);
    last = now;
    sim.tick(S, real * speed);

    const idle = (Date.now() - ui.lastInput) / 1000;
    if (idle >= R.afk.loseAfter) sim.forceEnd(S, 'lose', R.afk.loseAfter + '초 동안 입력이 없어 기권패 처리되었습니다.');
    if (ui.aim && Date.now() - ui.aim.at > R.aimTimeout * 1000) cancelAim(R.aimTimeout + '초 동안 입력이 없어 조준이 자동 취소되었습니다.');

    view.render();
    if (S.status !== 'run' && !finished) { finished = true; clearInterval(loop); onEnd(); }
  }, 100);

  // ── 전투 종료 → 전적 저장 → 결과 화면 ────────────────────────────────
  async function onEnd() {
    const win = S.status === 'win';
    $('#end').classList.remove('hidden');
    $('#end-title').textContent = win ? 'VICTORY' : 'DEFEAT';
    $('#end-title').style.color = win ? '#67e8f9' : '#f87171';
    $('#end-title').style.textShadow = '0 0 40px currentColor';
    $('#end-reason').textContent = S.endReason;
    $('#end-save').textContent = '전적 저장 중…';

    const result = {
      result: S.status, reason: S.endReason, stats: Object.assign({}, S.stats),
      durationSec: Math.floor(S.t), opponent: match.opponent.name, mode: match.mode, token: match.token,
    };
    WOW.state.setLastMatch(result);
    WOW.state.setMatch(null);

    const started = Date.now();
    try {
      result.record = await WOW.api.recordMatch({
        mode: result.mode, result: result.result, opponent: result.opponent, durationSec: result.durationSec,
        casts: result.stats.casts, damageDealt: result.stats.damageDealt, damageTaken: result.stats.damageTaken, matchToken: result.token,
      });
      $('#end-save').textContent = '전적을 저장했습니다.';
    } catch (e) {
      result.saveError = e.message;
      $('#end-save').textContent = '전적 저장 실패 — 결과 화면에서 다시 시도합니다.';
    }
    WOW.state.setLastMatch(result);
    setTimeout(function () { WOW.go('result'); }, Math.max(0, 2200 - (Date.now() - started)));
  }

  // ── 입력 ─────────────────────────────────────────────────────────
  window.addEventListener('keydown', function (e) {
    touch();
    const k = e.key.toLowerCase();
    if (ui.aim) {
      if (k === 'q') finishCast(ui.aim.slot, 0);
      else if (k === 'w') finishCast(ui.aim.slot, 1);
      else if (k === 'e') finishCast(ui.aim.slot, 2);
      else if (k === 'escape') cancelAim('조준을 취소했습니다. (쿨타임 소모 없음)');
      return;
    }
    if (/^[1-8]$/.test(k)) startCast(Number(k) - 1);
    else if (k === 'arrowleft' || k === 'a') sim.moveMe(S, -1);
    else if (k === 'arrowright' || k === 'd') sim.moveMe(S, 1);
  });
  window.addEventListener('pointerdown', touch);
  $('#btn-left').onclick = function () { touch(); sim.moveMe(S, -1); view.render(); };
  $('#btn-right').onclick = function () { touch(); sim.moveMe(S, 1); view.render(); };

  // 전투 중 페이지를 떠나려 하면 확인
  window.addEventListener('beforeunload', function (e) { if (!finished) { e.preventDefault(); e.returnValue = ''; } });

  // ── 개발용 QA 패널 (localhost 또는 ?dev=1) ─────────────────────────
  const devMode = ['localhost', '127.0.0.1'].includes(location.hostname) || WOW.query('dev') === '1';
  if (devMode) {
    $('#qa').classList.remove('hidden');
    WOW.$$('[data-speed]').forEach(function (b) {
      b.onclick = function () {
        speed = Number(b.getAttribute('data-speed'));
        WOW.$$('[data-speed]').forEach(function (x) { x.classList.toggle('on', x === b); });
      };
    });
    WOW.$('[data-speed="1"]').classList.add('on');
    $('#qa-afk').onclick = function () { ui.lastInput = Date.now() - 24000; };
    $('#qa-dc').onclick = function () { sim.forceEnd(S, 'lose', '연결이 끊어져 패배 처리되었습니다.'); };
    $('#qa-win').onclick = function () { sim.forceEnd(S, 'win', '(개발용) 강제 승리'); };
  }

  view.render();
});
