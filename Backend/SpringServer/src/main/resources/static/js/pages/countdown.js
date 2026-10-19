// 캔버스 11 · 매치 카운트다운
WOW.page({ auth: true, deck: true }, function (ctx) {
  WOW.decorate();
  const $ = WOW.$;

  // 매치 정보가 없으면(주소로 바로 들어온 경우) 봇 연습전으로 시작
  let match = WOW.state.getMatch();
  if (!match) {
    WOW.beginMatch('bot', { name: 'Bot', rating: ctx.profile.rating, isBot: true });
    match = WOW.state.getMatch();
  }
  const foe = match.opponent;
  const myTier = WOW.tierOf(ctx.profile.rating);
  const foeTier = WOW.tierOf(foe.rating);

  $('#me-name').textContent = ctx.profile.nickname;
  $('#me-tier').textContent = myTier.name + ' · ' + ctx.profile.rating;
  $('#me-tier').style.color = myTier.color;
  $('#me-deck').innerHTML = ctx.deck.map(function (id) { return WOW.skillIcon(id, 44); }).join('');
  $('#foe-name').textContent = foe.name;
  $('#foe-tier').textContent = foeTier.name + ' · ' + foe.rating;
  $('#foe-tier').style.color = foeTier.color;
  $('#foe-deck').innerHTML = new Array(8).fill('<div class="hex-clip unknown">?</div>').join('');

  let n = WOW.RULES.countdown;
  const el = $('#count');
  function show() {
    el.textContent = n > 0 ? n : 'START!';
    el.classList.toggle('start', n <= 0);
    el.classList.remove('result-enter'); void el.offsetWidth; el.classList.add('result-enter'); // 애니메이션 다시 재생
  }
  show();
  (function next() {
    setTimeout(function () {
      n--;
      if (n < 0) { WOW.go('battle'); return; }
      show();
      next();
    }, n === 0 ? 700 : 1000);
  })();
});
