// 캔버스 6 · 친선전 룸 (서버 룸 API + 2초 폴링)
WOW.page({ auth: true, deck: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const esc = WOW.esc;
  const me = ctx.profile;
  const deckErr = WOW.validateDeck(ctx.deck);
  const POLL_MS = 2000;

  let local = WOW.state.getRoom();   // { code, role }
  let room = null;                   // 서버 응답 (RoomResponse)
  let botGuest = false;              // 방장이 '봇으로 채우기' 를 눌렀는지 (서버에는 기록하지 않음)
  let friends = [];
  let timer = 0;

  WOW.$$('.deck-warn').forEach(function (el) {
    el.textContent = deckErr ? deckErr + ' 마법서에서 덱을 편성해주세요.' : '';
    el.classList.toggle('hidden', !deckErr);
  });

  function show(view) {
    ['none', 'host', 'guest'].forEach(function (v) { $('#view-' + v).classList.toggle('hidden', v !== view); });
  }

  function slot(label, name, sub, empty) {
    return '<div class="slot' + (empty ? ' empty' : '') + '">' +
      '<span class="label" style="color:var(--purple-500);font-size:.75rem">' + esc(label) + '</span>' +
      (empty ? '<span class="blink muted" style="font-size:1.1rem">대기 중…</span>'
             : '<span class="display" style="font-weight:700;font-size:1.25rem;color:var(--purple-100)">' + esc(name) + '</span>') +
      (sub ? '<span style="color:var(--cyan-400);font-size:.9rem">' + esc(sub) + '</span>' : '') + '</div>';
  }

  function leaveLocal(msg) {
    clearInterval(timer);
    WOW.state.setRoom(null);
    local = null; room = null; botGuest = false;
    if (msg) WOW.toast(msg, 'info');
    render();
  }

  // 대전 시작 → 카운트다운. 룸 정보는 더 이상 필요 없으므로 지운다.
  function startMatch(opponent) {
    clearInterval(timer);
    WOW.beginMatch('friendly', opponent, { roomCode: local.code });
    WOW.state.setRoom(null);
    WOW.go('countdown');
  }

  function render() {
    if (!local) { show('none'); return; }
    if (local.role === 'host') renderHost(); else renderGuest();
  }

  function renderHost() {
    show('host');
    $('#host-code').textContent = local.code;
    const realGuest = room && room.guestPlayerId ? room : null;
    const guestHtml = realGuest
      ? slot('게스트', realGuest.guestNickname, realGuest.guestReady ? '준비 완료' : '준비 중…')
      : botGuest ? slot('게스트', 'Bot', 'Bot') : slot('게스트', '', '', true);
    $('#host-slots').innerHTML = slot('호스트', me.nickname, '준비 완료') + guestHtml;
    $('#btn-bot').disabled = !!realGuest || botGuest;
    $('#btn-kick').classList.toggle('hidden', !realGuest && !botGuest);
    $('#btn-start').disabled = !!deckErr || !(botGuest || (realGuest && realGuest.guestReady));

    const online = friends.filter(function (f) { return ctx.online.includes(String(f.id)); });
    $('#invite-list').innerHTML = online.length === 0
      ? '<div class="muted">접속 중인 친구가 없습니다.</div>'
      : online.map(function (f) {
          return '<div class="list-row"><span class="dot on"></span><span class="grow" style="font-size:1.1rem;color:var(--purple-100)">' + esc(f.nickname) + '</span>' +
            '<button class="btn btn-secondary btn-sm" data-invite="' + f.id + '"' + (realGuest || botGuest ? ' disabled' : '') + '>초대</button></div>';
        }).join('');
  }

  function renderGuest() {
    show('guest');
    $('#guest-code').textContent = local.code;
    const ready = !!(room && room.guestReady);
    $('#guest-slots').innerHTML =
      slot('호스트', room ? room.hostNickname : '…', '대기 중') +
      slot('게스트 (나)', me.nickname, ready ? '준비 완료' : '준비 전');
    $('#btn-ready').disabled = !!deckErr;
    $('#btn-ready').textContent = ready ? '준비 취소 (호스트가 시작하길 기다리는 중…)' : '준비 완료';
  }

  // 서버 상태 확인
  async function poll() {
    if (!local) return;
    try {
      room = await WOW.api.getRoom(local.code);
    } catch (e) {
      if (e.status === 404) { leaveLocal('룸이 사라졌습니다.'); return; }
      return; // 일시적인 오류는 다음 주기에 재시도
    }
    if (room.status === 'closed') { leaveLocal(local.role === 'host' ? '룸이 닫혔습니다.' : '방장이 룸을 닫았습니다.'); return; }
    if (local.role === 'guest') {
      if (room.guestPlayerId !== me.id) { leaveLocal('룸에서 나가게 되었습니다.'); return; }
      if (room.status === 'in_game') { startMatch({ name: room.hostNickname || '호스트', rating: 1000, isBot: false }); return; }
    }
    if (local.role === 'host' && room.guestPlayerId && botGuest) botGuest = false;
    render();
  }

  function startPolling() {
    clearInterval(timer);
    poll();
    timer = setInterval(poll, POLL_MS);
  }

  // 룸 입장 (코드 입력 / 초대 우편 / ?code=)
  async function join(code) {
    code = (code || '').trim().toUpperCase();
    if (!WOW.ROOM_CODE_RE.test(code)) { WOW.toast('룸 코드는 영문 대문자·숫자 6자리입니다.', 'error'); return; }
    try {
      room = await WOW.api.joinRoom(code);
      local = { code: room.roomCode, role: room.hostPlayerId === me.id ? 'host' : 'guest' };
      WOW.state.setRoom(local);
      WOW.toast('룸 ' + local.code + ' 에 입장했습니다.', 'success');
      startPolling();
    } catch (e) { WOW.toast(e.message, 'error'); }
  }

  // ── 버튼 ──
  $('#btn-create').onclick = function () {
    WOW.run(this, async function () {
      room = await WOW.api.createRoom();
      local = { code: room.roomCode, role: 'host' };
      WOW.state.setRoom(local);
      botGuest = false;
      startPolling();
    });
  };

  const codeInput = $('#join-code');
  codeInput.oninput = function () {
    codeInput.value = codeInput.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 6);
    $('#btn-join').disabled = codeInput.value.length !== 6;
  };
  $('#join-form').addEventListener('submit', function (e) { e.preventDefault(); join(codeInput.value); });

  $('#btn-copy').onclick = async function () {
    try { await navigator.clipboard.writeText(local.code); WOW.toast('룸 코드를 복사했습니다.', 'success'); }
    catch (e) { WOW.toast('복사하지 못했습니다. 코드를 직접 전달해주세요.', 'error'); }
  };
  $('#btn-close').onclick = function () {
    WOW.run(this, async function () { await WOW.api.leaveRoom(local.code); leaveLocal('룸을 닫았습니다.'); });
  };
  $('#btn-leave').onclick = function () {
    WOW.run(this, async function () { await WOW.api.leaveRoom(local.code); leaveLocal(); });
  };
  $('#btn-bot').onclick = function () { botGuest = true; render(); };
  $('#btn-kick').onclick = function () {
    if (botGuest) { botGuest = false; render(); return; }
    WOW.run(this, async function () { room = await WOW.api.kickGuest(local.code); render(); });
  };
  $('#btn-ready').onclick = function () {
    const next = !(room && room.guestReady);
    WOW.run(this, async function () { room = await WOW.api.readyRoom(local.code, next); render(); });
  };
  $('#btn-start').onclick = function () {
    if (deckErr) { WOW.toast(deckErr, 'error'); return; }
    if (botGuest) { startMatch({ name: 'Bot', rating: me.rating, isBot: true }); return; }
    WOW.run(this, async function () {
      room = await WOW.api.startRoom(local.code);
      startMatch({ name: room.guestNickname, rating: room.guestRating || 1000, isBot: false });
    });
  };
  $('#invite-list').addEventListener('click', function (e) {
    const btn = e.target.closest('[data-invite]');
    if (!btn) return;
    const f = friends.find(function (x) { return String(x.id) === btn.getAttribute('data-invite'); });
    WOW.run(btn, function () { return WOW.api.sendInvite(f.id, local.code); }, f.nickname + ' 님에게 초대장을 보냈습니다.');
  });

  // ── 시작 ──
  WOW.api.friends().then(function (l) {
    friends = l.filter(function (f) { return f.relation === 'friend'; });
    render();
  }).catch(function () { /* 무시 */ });
  WOW.onPresence(function () { if (local && local.role === 'host') renderHost(); });

  const codeParam = WOW.query('code');
  if (codeParam && (!local || local.code !== codeParam.toUpperCase())) {
    render();
    await join(codeParam);
  } else if (local) {
    startPolling();
  } else {
    render();
  }
});
