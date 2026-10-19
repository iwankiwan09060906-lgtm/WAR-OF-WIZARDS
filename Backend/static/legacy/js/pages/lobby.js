// 캔버스 2 · 메인 로비
WOW.page({ auth: true, deck: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const p = ctx.profile;
  const deckErr = WOW.validateDeck(ctx.deck);

  const welcome = sessionStorage.getItem('wow.welcome');
  if (welcome) { sessionStorage.removeItem('wow.welcome'); WOW.toast(welcome + ' 마법사님, 환영합니다!', 'success'); }

  // 서버 game_configs 의 한 줄 메시지 / 점검 모드 (관리자가 바꾸면 실시간 반영)
  function showMotd() {
    const box = $('#motd');
    const maintenance = WOW.config.maintenance === 'true';
    const text = maintenance ? '🔧 서버 점검 중입니다. 대전이 제한될 수 있습니다.' : (WOW.config.motd || '');
    box.textContent = text;
    box.classList.toggle('hidden', !text);
    box.classList.toggle('warn', maintenance);
  }
  showMotd();
  window.addEventListener('wow:live', function (e) { if (e.detail.type === 'config') showMotd(); });

  // 헤더: 닉네임 / 등급
  const tier = WOW.tierOf(p.rating);
  $('#my-name').textContent = p.nickname;
  $('#my-tier').textContent = tier.name + ' · ' + p.rating;
  $('#my-tier').style.color = tier.color;
  $('#btn-logout').onclick = WOW.logout;
  if (WOW.isAdmin()) $('#btn-admin').classList.remove('hidden');

  // 덱 상태
  const deckView = $('#deck-view');
  if (ctx.deck.length === 0) deckView.innerHTML = '<div class="warn">아직 덱이 없습니다. 마법서에서 스킬 8장을 편성해야 대전을 시작할 수 있어요.</div>';
  else if (deckErr) deckView.innerHTML = '<div class="warn">' + WOW.esc(deckErr) + '</div>';
  else deckView.innerHTML = '<div class="row wrap gap-s">' + ctx.deck.map(function (id) { return WOW.skillIcon(id, 46); }).join('') + '</div>';

  // 대전 버튼
  $('#btn-ranked').disabled = !!deckErr;
  $('#btn-bot').disabled = !!deckErr;
  $('#btn-ranked').onclick = function () {
    WOW.beginMatch('ranked', { name: '탐색 중…', rating: 0, isBot: false });
    WOW.go('queue');
  };
  $('#btn-bot').onclick = function () {
    WOW.beginMatch('bot', { name: 'Bot', rating: p.rating, isBot: true });
    WOW.go('countdown');
  };

  // 타일 정보
  $('#tile-record').textContent = p.wins + '승 ' + p.losses + '패 · 승률 ' + WOW.winRate(p.wins, p.losses) + '%';

  WOW.api.mails().then(function (mails) {
    const unread = mails.filter(function (m) { return !m.read; }).length;
    $('#tile-mail').textContent = unread > 0 ? '읽지 않음 ' + unread + '건' : '새 우편 없음';
    const badge = $('#mail-badge');
    badge.textContent = unread;
    badge.classList.toggle('hidden', unread === 0);
  }).catch(function () { /* 무시 */ });

  let friends = [];
  WOW.api.friends().then(function (list) {
    friends = list.filter(function (f) { return f.relation === 'friend'; });
    showOnline();
  }).catch(function () { /* 무시 */ });
  function showOnline() {
    const n = friends.filter(function (f) { return ctx.online.includes(String(f.id)); }).length;
    $('#tile-friends').textContent = '친구 ' + friends.length + '명 · 접속 중 ' + n + '명';
  }
  WOW.onPresence(showOnline);
});
