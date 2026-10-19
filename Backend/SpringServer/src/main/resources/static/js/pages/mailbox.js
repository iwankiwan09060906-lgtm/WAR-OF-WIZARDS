// 캔버스 7 · 우편함/공지 (id 는 "mail-12" / "notice-3" 형태)
WOW.page({ auth: true }, async function () {
  WOW.decorate();
  const $ = WOW.$;
  const esc = WOW.esc;
  const KIND_LABEL = { mail: '우편', invite: '초대', notice: '공지' };
  const KIND_COLOR = { mail: '#a78bfa', invite: '#22d3ee', notice: '#fbbf24' };

  let mails = [];
  let tab = 'mail';
  let selId = null;

  const inTab = function (m, t) { return t === 'notice' ? m.kind === 'notice' : m.kind !== 'notice'; };
  const tagHtml = function (kind) { return '<span class="tag" style="color:' + KIND_COLOR[kind] + ';background:' + KIND_COLOR[kind] + '22">' + KIND_LABEL[kind] + '</span>'; };

  async function reload() {
    try { mails = await WOW.api.mails(); render(); }
    catch (e) { WOW.toast(e.message, 'error'); }
  }

  async function markRead(m) {
    if (m.read) return;
    m.read = true;
    render();
    try { await WOW.api.markMailRead(m.id); } catch (e) { WOW.toast(e.message, 'error'); }
  }

  function render() {
    // 탭 + 안 읽은 수
    WOW.$$('.tab').forEach(function (b) {
      const t = b.getAttribute('data-tab');
      const n = mails.filter(function (m) { return !m.read && inTab(m, t); }).length;
      b.classList.toggle('active', t === tab);
      const badge = $('.badge', b);
      badge.textContent = n;
      badge.classList.toggle('hidden', n === 0);
    });

    const list = mails.filter(function (m) { return inTab(m, tab); });
    $('#mail-list').innerHTML = list.length === 0
      ? '<div class="dim" style="padding:24px">' + (tab === 'mail' ? '받은 우편이 없습니다.' : '등록된 공지가 없습니다.') + '</div>'
      : list.map(function (m) {
          return '<button class="mail-item' + (m.id === selId ? ' selected' : '') + '" data-id="' + esc(m.id) + '">' +
            '<div class="row gap-s" style="margin-bottom:4px">' + (m.read ? '' : '<span class="unread-dot"></span>') + tagHtml(m.kind) +
            '<span class="muted" style="margin-left:auto;font-size:.8rem">' + WOW.timeAgo(m.createdAt) + '</span></div>' +
            '<div class="truncate" style="font-size:1.1rem;' + (m.read ? 'color:var(--purple-400)' : 'color:var(--purple-100);font-weight:600') + '">' + esc(m.title) + '</div></button>';
        }).join('');

    const sel = list.find(function (m) { return m.id === selId; });
    if (!sel) { $('#detail').innerHTML = '<div class="center dim" style="height:100%;font-size:1.1rem">왼쪽에서 항목을 선택하세요.</div>'; return; }

    let html = '<div class="col gap-m" style="max-width:720px">' +
      '<div>' + tagHtml(sel.kind) +
      '<h3 class="display" style="font-size:1.5rem;color:var(--purple-100);margin-top:12px">' + esc(sel.title) + '</h3>' +
      '<div class="muted" style="font-size:.9rem;margin-top:4px">' + (sel.from ? '보낸 사람: ' + esc(sel.from) + ' · ' : '') + WOW.timeAgo(sel.createdAt) + '</div></div>' +
      '<p style="font-size:1.1rem;color:var(--purple-200);line-height:1.7;white-space:pre-line">' + esc(sel.body) + '</p>';
    if (sel.reward) {
      html += '<div class="reward-box between"><div><div class="label" style="color:#f59e0b">첨부 보상</div>' +
        '<div class="display" style="font-weight:700;font-size:1.25rem;color:#fcd34d">' + esc(sel.reward) + '</div></div>' +
        '<button class="btn btn-primary btn-sm" id="btn-claim"' + (sel.read ? ' disabled' : '') + '>' + (sel.read ? '수령 완료' : '수령') + '</button></div>';
    }
    if (sel.kind === 'invite' && sel.roomCode) {
      html += '<div class="invite-box between"><div><div class="label" style="color:var(--cyan)">룸 코드</div>' +
        '<div class="room-code" style="font-size:1.9rem">' + esc(sel.roomCode) + '</div></div>' +
        '<button class="btn btn-secondary btn-sm" id="btn-join">바로 입장</button></div>';
    }
    if (sel.kind !== 'notice') {
      html += '<div><button class="btn btn-danger btn-sm" id="btn-delete">우편 삭제</button></div>';
    }
    $('#detail').innerHTML = html + '</div>';

    if ($('#btn-claim')) $('#btn-claim').onclick = function () { markRead(sel); WOW.toast('보상을 수령했습니다: ' + sel.reward, 'success'); };
    if ($('#btn-join')) $('#btn-join').onclick = function () { markRead(sel); WOW.go('room', { code: sel.roomCode }); };
    if ($('#btn-delete')) $('#btn-delete').onclick = function () {
      if (sel.reward && !sel.read && !confirm('아직 수령하지 않은 보상이 있습니다. 삭제할까요?')) return;
      WOW.run(this, async function () {
        await WOW.api.deleteMail(sel.id);
        mails = mails.filter(function (m) { return m.id !== sel.id; });
        selId = null;
        render();
      }, '우편을 삭제했습니다.');
    };
  }

  WOW.$$('.tab').forEach(function (b) {
    b.onclick = function () { tab = b.getAttribute('data-tab'); selId = null; render(); };
  });

  $('#mail-list').addEventListener('click', function (e) {
    const item = e.target.closest('[data-id]');
    if (!item) return;
    const m = mails.find(function (x) { return x.id === item.getAttribute('data-id'); });
    selId = m.id;
    if (!m.reward) markRead(m); else render(); // 보상 우편은 '수령'을 눌러야 읽음 처리
  });

  $('#btn-read-all').onclick = function () {
    const targets = mails.filter(function (m) { return !m.read && !m.reward; });
    if (targets.length === 0) { WOW.toast('읽지 않은 우편이 없습니다. (보상 우편은 직접 수령하세요)', 'info'); return; }
    WOW.run(this, async function () {
      await Promise.all(targets.map(function (m) { return WOW.api.markMailRead(m.id); }));
      targets.forEach(function (m) { m.read = true; });
      render();
    }, targets.length + '건을 읽음 처리했습니다.');
  };

  await reload();
});
