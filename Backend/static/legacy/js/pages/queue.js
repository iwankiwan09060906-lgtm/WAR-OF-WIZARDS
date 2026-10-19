// 캔버스 10 · 랭킹전 매칭 대기 (서버 매칭 대기열 사용)
WOW.page({ auth: true, deck: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const POLL_MS = 2000;
  const TIPS = [
    '적 타워가 살아있는 동안에는 상대 플레이어에게 20%의 피해만 들어갑니다.',
    '플레이어 스킬은 타워·넥서스를 공격할 수 없어요. 유닛으로 구조물을 밀어붙이세요.',
    '고위력 스킬은 게임당 3회까지만 쓸 수 있습니다. 결정적인 순간에 아껴두세요.',
    '왼손을 옆으로 휘두르면 좌/중/우 열을 한 칸씩 순간이동합니다.',
    '적 타워를 부수면 공격력이 영구 +20%, 넥서스를 부수면 +40% 강해집니다.',
    '오래 기다릴수록 상대를 찾는 레이팅 범위가 넓어집니다.',
  ];

  const deckErr = WOW.validateDeck(ctx.deck);
  if (deckErr) { WOW.toast(deckErr, 'error'); setTimeout(function () { WOW.go('spellbook'); }, 1200); return; }

  const p = ctx.profile;
  const tier = WOW.tierOf(p.rating);
  $('#me-avatar').textContent = p.nickname.slice(0, 1).toUpperCase();
  $('#me-name').textContent = p.nickname;
  $('#me-tier').textContent = tier.name + ' · ' + p.rating;
  $('#me-tier').style.color = tier.color;
  $('#me-deck').innerHTML = ctx.deck.map(function (id) { return WOW.skillIcon(id, 30); }).join('');

  let tipIdx = Math.floor(Math.random() * TIPS.length);
  function nextTip() { $('#tip').textContent = '💡 ' + TIPS[tipIdx++ % TIPS.length]; }
  nextTip();
  const tipTimer = setInterval(nextTip, 7000);

  let startedAt = Date.now();   // 서버 대기 시간으로 보정
  let last = null;              // 마지막 서버 응답
  let pollTimer = 0;
  let done = false;

  function step(name) {
    const order = ['join', 'search', 'found'];
    WOW.$$('.queue-step').forEach(function (el) {
      const i = order.indexOf(el.getAttribute('data-step'));
      const cur = order.indexOf(name);
      el.classList.toggle('done', i < cur);
      el.classList.toggle('on', i === cur);
    });
  }

  // 1초마다 시계/범위 막대를 부드럽게 갱신 (서버 응답은 2초마다)
  const clock = setInterval(function () {
    if (done) return;
    const sec = (Date.now() - startedAt) / 1000;
    $('#elapsed').textContent = WOW.fmtTime(sec);
    if (!last) return;
    const range = 50 + Math.floor(sec) * 15;
    const botLeft = Math.max(0, last.botTimeoutSec - sec);
    $('#st-range').textContent = '±' + range;
    $('#st-bot').textContent = Math.ceil(botLeft) + '초';
    $('#range-bar').innerHTML = WOW.bar(Math.min(sec, last.botTimeoutSec), last.botTimeoutSec, 'cyan',
      { label: '레이팅 ' + (p.rating - range) + ' ~ ' + (p.rating + range) + ' 탐색 중', height: 8 });
  }, 250);

  function stopAll() {
    done = true;
    clearInterval(pollTimer); clearInterval(clock); clearInterval(tipTimer);
  }

  function onMatched(s) {
    stopAll();
    step('found');
    const foeTier = WOW.tierOf(s.opponentRating);
    $('#title').textContent = s.bot ? '봇 매칭 완료' : '상대를 찾았습니다!';
    $('#radar').classList.add('done');
    $('#radar-core').textContent = '✓';
    $('#foe-card').classList.add('found');
    $('#foe-avatar').classList.remove('unknown');
    $('#foe-avatar').classList.add('foe');
    $('#foe-avatar').textContent = s.bot ? '🤖' : s.opponentNickname.slice(0, 1).toUpperCase();
    $('#foe-name').textContent = s.opponentNickname;
    $('#foe-tier').className = '';
    $('#foe-tier').textContent = foeTier.name + ' · ' + s.opponentRating;
    $('#foe-tier').style.color = foeTier.color;
    $('#status').className = s.bot ? 'warn' : 'ok';
    $('#status').textContent = s.bot ? '대기 시간이 지나 비슷한 레이팅의 봇과 매칭되었습니다.' : '곧 대전이 시작됩니다. 준비하세요!';
    $('#btn-cancel').classList.add('hidden');
    $('#range-box').classList.add('hidden');

    WOW.beginMatch('ranked',
      { name: s.opponentNickname, rating: s.opponentRating, isBot: s.bot, playerId: s.opponentPlayerId },
      { matchId: s.matchId });
    setTimeout(function () { WOW.go('countdown'); }, 1800);
  }

  function apply(s) {
    last = s;
    $('#st-queue').textContent = s.queueSize;
    if (s.state === 'matched') { onMatched(s); return; }
    if (s.state === 'none') {  // 서버에서 대기열이 사라짐 (오래 응답이 없었거나 서버 재시작)
      stopAll();
      WOW.toast('매칭 대기열에서 빠졌습니다. 다시 시도해주세요.', 'error');
      setTimeout(function () { WOW.go('lobby'); }, 1500);
      return;
    }
    startedAt = Date.now() - s.waitedSec * 1000;
    step('search');
    $('#status').textContent = '같은 대기열의 마법사 중 레이팅이 가까운 상대를 찾고 있습니다.';
  }

  async function poll() {
    try { apply(await WOW.api.queueStatus()); }
    catch (e) { $('#status').textContent = '서버 응답이 늦습니다… (' + e.message + ')'; }
  }

  // 취소: 대기열에서 빠진 뒤 로비로
  $('#btn-cancel').onclick = async function () {
    stopAll();
    this.disabled = true;
    try { await WOW.api.leaveQueue(); } catch (e) { /* 이미 빠졌을 수 있음 */ }
    WOW.go('lobby');
  };
  // 창을 닫으면 대기열에서 빠지도록 요청 (실패해도 서버가 10초 뒤 정리)
  window.addEventListener('pagehide', function () {
    if (done) return;
    try { fetch('/api/matchmaking/queue', { method: 'DELETE', keepalive: true, headers: { Authorization: 'Bearer ' + WOW.api.getToken() } }); } catch (e) { /* 무시 */ }
  });

  // 시작
  step('join');
  $('#status').textContent = '대기열에 참가하는 중…';
  try {
    apply(await WOW.api.joinQueue());
  } catch (e) {
    stopAll();
    WOW.showError({ title: '매칭을 시작할 수 없습니다', message: e.message, retry: true });
    return;
  }
  if (!done) pollTimer = setInterval(poll, POLL_MS);
});
