// 캔버스 8 · 랭킹 (Top 100)
WOW.page({ auth: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const MEDAL = ['#fbbf24', '#cbd5e1', '#d97706'];
  let rows = [];

  function render() {
    const q = $('#search').value.trim().toLowerCase();
    const list = rows.filter(function (r) { return !q || r.nickname.toLowerCase().includes(q); });
    $('#rows').innerHTML = list.length === 0
      ? '<div class="dim" style="padding:24px">' + (q ? '검색 결과가 없습니다.' : '랭킹 정보가 없습니다.') + '</div>'
      : list.map(function (r) {
          const me = r.playerId === ctx.profile.id;
          const t = WOW.tierOf(r.rating);
          return '<div class="rank-row' + (me ? ' me' : '') + '">' +
            '<span class="display" style="font-weight:900;font-size:1.25rem;color:' + (r.rank <= 3 ? MEDAL[r.rank - 1] : '#8b80c8') + '">' + r.rank + '</span>' +
            '<span class="truncate" style="color:var(--purple-100)">' + WOW.esc(r.nickname) + (me ? ' <span class="me-tag">나</span>' : '') + (r.bot ? ' <small class="muted">BOT</small>' : '') + '</span>' +
            '<span style="color:' + t.color + '">' + t.name + '</span>' +
            '<span class="display" style="font-weight:700;color:var(--purple-100)">' + r.rating + '</span>' +
            '<span class="dim">' + r.wins + '승 ' + r.losses + '패</span>' +
            '<span class="dim text-right">' + WOW.winRate(r.wins, r.losses) + '%</span></div>';
        }).join('');

    const mine = rows.find(function (r) { return r.playerId === ctx.profile.id; });
    $('#mine').innerHTML = mine
      ? '<span class="ok" style="font-weight:600">' + mine.rank + '위 · ' + mine.rating + '점 · ' + mine.wins + '승 ' + mine.losses + '패</span>'
      : '<span style="color:var(--purple-300)">Top 100 밖 · ' + ctx.profile.rating + '점</span>';
  }

  $('#search').oninput = render;

  try {
    rows = await WOW.api.ranking(100);
    render();
  } catch (e) {
    $('#rows').innerHTML = '<div class="err" style="padding:24px">' + WOW.esc(e.message) + '</div>';
  }
});
