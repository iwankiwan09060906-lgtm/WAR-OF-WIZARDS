// 캔버스 5 · 친구 목록
WOW.page({ auth: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const esc = WOW.esc;
  const room = WOW.state.getRoom();
  let list = [];

  if (room) $('#btn-room').textContent = '룸 ' + room.code;

  async function reload() {
    try { list = await WOW.api.friends(); render(); }
    catch (e) { WOW.toast(e.message, 'error'); }
    finally { $('#loading-text').classList.add('hidden'); }
  }

  function isOnline(f) { return ctx.online.includes(String(f.id)); }

  function render() {
    const friends = list.filter(function (f) { return f.relation === 'friend'; })
      .sort(function (a, b) { return Number(isOnline(b)) - Number(isOnline(a)) || a.nickname.localeCompare(b.nickname); });
    const incoming = list.filter(function (f) { return f.relation === 'incoming'; });
    const outgoing = list.filter(function (f) { return f.relation === 'outgoing'; });

    $('#sec-incoming').classList.toggle('hidden', incoming.length === 0);
    $('#cnt-incoming').textContent = incoming.length;
    $('#list-incoming').innerHTML = incoming.map(function (f) {
      return '<div class="list-row"><span class="grow" style="color:var(--purple-100);font-size:1.1rem">' + esc(f.nickname) +
        ' <small class="muted">· ' + f.rating + '</small></span>' +
        '<button class="btn btn-secondary btn-sm" data-act="accept" data-id="' + f.id + '">수락</button>' +
        '<button class="btn btn-ghost btn-sm" data-act="reject" data-id="' + f.id + '">거절</button></div>';
    }).join('');

    $('#cnt-friends').textContent = friends.length;
    $('#list-friends').innerHTML = friends.length === 0
      ? '<div class="dim" style="padding:16px 0">아직 친구가 없습니다. 닉네임으로 친구 요청을 보내보세요.</div>'
      : friends.map(function (f) {
          const on = isOnline(f);
          const t = WOW.tierOf(f.rating);
          return '<div class="list-row"><span class="dot' + (on ? ' on' : '') + '" title="' + (on ? '접속 중' : '오프라인') + '"></span>' +
            '<span class="grow" style="color:var(--purple-100);font-size:1.1rem">' + esc(f.nickname) +
            ' <small style="color:' + t.color + '">' + t.name + ' · ' + f.rating + '</small></span>' +
            '<span class="muted" style="width:64px;text-align:right">' + (on ? '접속 중' : '오프라인') + '</span>' +
            '<button class="btn btn-secondary btn-sm" data-act="invite" data-id="' + f.id + '"' + (on ? '' : ' disabled') + '>초대</button>' +
            '<button class="btn btn-ghost btn-sm" data-act="remove" data-id="' + f.id + '">삭제</button></div>';
        }).join('');

    $('#sec-outgoing').classList.toggle('hidden', outgoing.length === 0);
    $('#cnt-outgoing').textContent = outgoing.length;
    $('#list-outgoing').innerHTML = outgoing.map(function (f) {
      return '<div class="list-row"><span class="grow dim" style="font-size:1.1rem">' + esc(f.nickname) + '</span>' +
        '<span class="muted">수락 대기 중</span>' +
        '<button class="btn btn-ghost btn-sm" data-act="cancel" data-id="' + f.id + '">취소</button></div>';
    }).join('');
  }

  // 버튼 클릭은 한 곳에서 처리 (data-act 로 구분)
  document.addEventListener('click', function (e) {
    const btn = e.target.closest('[data-act]');
    if (!btn) return;
    const f = list.find(function (x) { return String(x.id) === btn.getAttribute('data-id'); });
    if (!f) return;
    const act = btn.getAttribute('data-act');
    if (act === 'accept') WOW.run(btn, function () { return WOW.api.respondFriend(f.id, true).then(reload); }, f.nickname + ' 님과 친구가 되었습니다.');
    if (act === 'reject') WOW.run(btn, function () { return WOW.api.respondFriend(f.id, false).then(reload); });
    if (act === 'remove') {
      if (!confirm(f.nickname + ' 님을 친구에서 삭제할까요?')) return;
      WOW.run(btn, function () { return WOW.api.removeFriend(f.id).then(reload); }, f.nickname + ' 님을 삭제했습니다.');
    }
    if (act === 'cancel') WOW.run(btn, function () { return WOW.api.removeFriend(f.id).then(reload); });
    if (act === 'invite') {
      if (!room || room.role !== 'host') { WOW.toast('먼저 친선전 룸을 만들어야 초대할 수 있습니다.', 'error'); return; }
      WOW.run(btn, function () { return WOW.api.sendInvite(f.id, room.code); }, f.nickname + ' 님에게 초대장을 보냈습니다. (룸 ' + room.code + ')');
    }
  });

  $('#request-form').addEventListener('submit', function (e) {
    e.preventDefault();
    const n = $('#nick').value.trim();
    if (n.length < 2) { WOW.toast('닉네임을 2자 이상 입력해주세요.', 'error'); return; }
    WOW.run(e.submitter, async function () {
      await WOW.api.requestFriend(n);
      $('#nick').value = '';
      await reload();
    }, n + ' 님에게 친구 요청을 보냈습니다.');
  });

  WOW.onPresence(render);
  await reload();
});
