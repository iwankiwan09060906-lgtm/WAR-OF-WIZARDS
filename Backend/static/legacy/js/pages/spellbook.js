// 캔버스 9 · 마법서 (덱 편성 → PUT /api/decks/me)
WOW.page({ auth: true, deck: true }, function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const esc = WOW.esc;
  const R = WOW.RULES;

  let saved = ctx.deck.slice();
  let sel = ctx.deck.slice();
  let filter = 'all';
  let focusId = sel[0] || 'S03';

  const highCount = function () { return sel.filter(function (id) { return WOW.getSkill(id).tier === 'high'; }).length; };
  const dirty = function () { return sel.length !== saved.length || sel.some(function (id, i) { return id !== saved[i]; }); };
  const cdOf = function (k) { return k.tier === 'high' ? R.cooldown.high : R.cooldown.normal; };

  function toggle(k) {
    focusId = k.id;
    if (sel.includes(k.id)) { sel = sel.filter(function (x) { return x !== k.id; }); render(); return; }
    if (sel.length >= WOW.DECK_SIZE) { WOW.toast('덱이 가득 찼습니다. (' + WOW.DECK_SIZE + '/' + WOW.DECK_SIZE + ') 다른 스킬을 먼저 빼주세요.', 'error'); render(); return; }
    if (k.tier === 'high' && highCount() >= WOW.MAX_HIGH_IN_DECK) { WOW.toast('고위력 스킬은 덱에 최대 ' + WOW.MAX_HIGH_IN_DECK + '장까지 넣을 수 있습니다.', 'error'); render(); return; }
    sel.push(k.id);
    render();
  }

  function renderPool() {
    const list = WOW.SKILLS.filter(function (k) { return filter === 'all' || k.tier === filter; });
    $('#pool').innerHTML = list.map(function (k) {
      const on = sel.includes(k.id);
      return '<button class="pool-card" data-id="' + k.id + '" style="border-color:' + (on ? k.color + '99' : 'rgba(124,58,237,0.2)') +
        ';background:' + (on ? k.color + '18' : 'rgba(0,0,0,0.3)') + ';box-shadow:' + (on ? '0 0 16px ' + WOW.glowOf(k.color, 0.25) : 'none') + '">' +
        WOW.skillIcon(k.id, 54, { active: on }) +
        '<div style="min-width:0"><div class="display" style="font-weight:700;color:' + (on ? k.color : '#c4b5fd') + '">' + esc(k.name) + '</div>' +
        '<div class="muted" style="font-size:.75rem;letter-spacing:.1em">' + k.id + ' · ' + WOW.KIND_META[k.kind].label + ' · ' + cdOf(k) + 's</div></div>' +
        (on ? '<span class="check" style="background:' + k.color + '">✓</span>' : '') + '</button>';
    }).join('');
  }

  function renderDetail() {
    const k = WOW.getSkill(focusId);
    $('#detail').innerHTML =
      '<div class="row gap-m">' + WOW.skillIcon(k.id, 72) +
      '<div><div class="display" style="font-weight:700;font-size:1.5rem;color:' + k.color + '">' + esc(k.name) + '</div>' +
      '<div class="dim" style="font-size:.9rem">' + k.id + ' · ' + (k.tier === 'high' ? '고위력' : '일반') + ' · ' + WOW.KIND_META[k.kind].label + '</div></div></div>' +
      '<p style="color:var(--purple-200);margin-top:12px;line-height:1.6">' + esc(k.effect) + '</p>' +
      '<div class="grid-3 mt-s" style="gap:8px">' +
      '<div class="stat-box"><b style="font-size:.9rem;font-family:var(--font-body)">' + cdOf(k) + '초</b><span>쿨타임</span></div>' +
      '<div class="stat-box"><b style="font-size:.9rem;font-family:var(--font-body)">' + WOW.KIND_META[k.kind].aim.split(' (')[0] + '</b><span>조준</span></div>' +
      '<div class="stat-box"><b style="font-size:.9rem;font-family:var(--font-body)">' + (k.tier === 'high' ? '게임당 ' + R.highUsesPerGame + '회' : '없음') + '</b><span>제한</span></div></div>';
  }

  function renderDeck() {
    let html = '';
    for (let i = 0; i < WOW.DECK_SIZE; i++) {
      const id = sel[i];
      html += '<div class="deck-slot"><span class="muted" style="font-size:10px;align-self:flex-start">' + (i + 1) + '</span>';
      if (id) {
        const k = WOW.getSkill(id);
        html += WOW.skillIcon(id, 40) +
          '<span class="display" style="font-size:10px;text-align:center;color:' + k.color + '">' + esc(k.name) + '</span>' +
          '<div class="row" style="gap:4px">' +
          '<button class="mini" data-move="' + i + '" data-dir="-1" aria-label="앞으로"' + (i === 0 ? ' disabled' : '') + '>◀</button>' +
          '<button class="mini" data-move="' + i + '" data-dir="1" aria-label="뒤로"' + (i === sel.length - 1 ? ' disabled' : '') + '>▶</button>' +
          '<button class="mini red" data-remove="' + id + '" aria-label="빼기">×</button></div>';
      } else {
        html += '<div class="empty-slot"></div>';
      }
      html += '</div>';
    }
    $('#deck').innerHTML = html;
    const err = WOW.validateDeck(sel);
    $('#deck-err').textContent = sel.length > 0 && err ? err : '';
  }

  function render() {
    const hc = highCount();
    $('#count-pill').textContent = sel.length + ' / ' + WOW.DECK_SIZE;
    $('#count-pill').className = 'pill' + (sel.length === WOW.DECK_SIZE ? ' pill-cyan' : '');
    $('#high-pill').textContent = '★ ' + hc + ' / ' + WOW.MAX_HIGH_IN_DECK;
    $('#high-pill').className = 'pill' + (hc >= WOW.MAX_HIGH_IN_DECK ? ' pill-amber' : '');
    $('#btn-back').textContent = dirty() ? '← 로비 (저장 안 됨)' : '← 로비';
    const err = WOW.validateDeck(sel);
    $('#btn-save').disabled = !!err || !dirty();
    $('#btn-save').textContent = dirty() ? '덱 저장' : '저장됨 ✓';
    $('#btn-clear').disabled = sel.length === 0;
    WOW.$$('.filter').forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-filter') === filter); });
    renderPool();
    renderDetail();
    renderDeck();
  }

  // ── 이벤트 ──
  $('#pool').addEventListener('click', function (e) {
    const b = e.target.closest('[data-id]');
    if (b) toggle(WOW.getSkill(b.getAttribute('data-id')));
  });
  $('#pool').addEventListener('mouseover', function (e) {
    const b = e.target.closest('[data-id]');
    if (b && b.getAttribute('data-id') !== focusId) { focusId = b.getAttribute('data-id'); renderDetail(); }
  });
  $('#deck').addEventListener('click', function (e) {
    const mv = e.target.closest('[data-move]');
    const rm = e.target.closest('[data-remove]');
    if (mv) {
      const i = Number(mv.getAttribute('data-move'));
      const j = i + Number(mv.getAttribute('data-dir'));
      if (j < 0 || j >= sel.length) return;
      const t = sel[i]; sel[i] = sel[j]; sel[j] = t;
      render();
    }
    if (rm) { sel = sel.filter(function (x) { return x !== rm.getAttribute('data-remove'); }); render(); }
  });
  WOW.$$('.filter').forEach(function (b) { b.onclick = function () { filter = b.getAttribute('data-filter'); render(); }; });
  $('#btn-clear').onclick = function () { sel = []; render(); };

  // 추천: 고위력 2장 + 일반 6장을 무작위로
  $('#btn-random').onclick = function () {
    const shuffle = function (a) { return a.map(function (x) { return [Math.random(), x]; }).sort(function (p, q) { return p[0] - q[0]; }).map(function (p) { return p[1]; }); };
    const normal = shuffle(WOW.SKILLS.filter(function (k) { return k.tier === 'normal'; })).slice(0, 6);
    const high = shuffle(WOW.SKILLS.filter(function (k) { return k.tier === 'high'; })).slice(0, 2);
    sel = normal.concat(high).map(function (k) { return k.id; });
    focusId = sel[0];
    render();
  };

  $('#btn-save').onclick = function () {
    const err = WOW.validateDeck(sel);
    if (err) { WOW.toast(err, 'error'); return; }
    WOW.run(this, async function () {
      await WOW.api.saveDeck(sel);
      saved = sel.slice();
      render();
    }, '마법서를 저장했습니다.');
  };

  // 저장하지 않고 나가려 하면 확인
  window.addEventListener('beforeunload', function (e) { if (dirty()) { e.preventDefault(); e.returnValue = ''; } });

  render();
});
