// 캔버스 3 · 마이페이지
WOW.page({ auth: true, deck: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  let matches = [];

  function renderProfile(p) {
    const tier = WOW.tierOf(p.rating);
    const rate = WOW.winRate(p.wins, p.losses);
    $('#avatar').textContent = p.nickname.slice(0, 1).toUpperCase();
    $('#nickname').textContent = p.nickname;
    $('#tier-pill').textContent = tier.name + ' · ' + p.rating;
    $('#tier-pill').style.color = tier.color;
    $('#tier-pill').style.borderColor = tier.color + '88';
    $('#st-total').textContent = p.wins + p.losses;
    $('#st-wins').textContent = p.wins;
    $('#st-losses').textContent = p.losses;
    // 승률 링
    const r = 46, c = 2 * Math.PI * r;
    $('#ring').innerHTML =
      '<svg width="120" height="120" viewBox="0 0 120 120" role="img" aria-label="승률 ' + rate + '%">' +
      '<circle cx="60" cy="60" r="' + r + '" fill="none" stroke="rgba(124,58,237,0.18)" stroke-width="10"/>' +
      '<circle cx="60" cy="60" r="' + r + '" fill="none" stroke="#06b6d4" stroke-width="10" stroke-linecap="round" stroke-dasharray="' + (c * rate / 100) + ' ' + c + '" transform="rotate(-90 60 60)" style="filter:drop-shadow(0 0 6px rgba(6,182,212,0.7))"/>' +
      '<text x="60" y="58" text-anchor="middle" fill="#e8e4ff" font-size="26" font-weight="700" font-family="Cinzel, serif">' + rate + '%</text>' +
      '<text x="60" y="78" text-anchor="middle" fill="#8b80c8" font-size="11" font-family="Rajdhani, sans-serif">WIN RATE</text></svg>';
  }
  renderProfile(ctx.profile);

  // 닉네임 변경 (PUT /api/players/me)
  $('#btn-edit-nick').onclick = function () {
    $('#nick-form').classList.remove('hidden');
    $('#nick-input').value = ctx.profile.nickname;
    $('#nick-input').focus();
  };
  $('#btn-nick-cancel').onclick = function () { $('#nick-form').classList.add('hidden'); };
  $('#nick-form').addEventListener('submit', async function (e) {
    e.preventDefault();
    const nick = $('#nick-input').value.trim();
    if (!/^[A-Za-z0-9가-힣_]{2,12}$/.test(nick)) { WOW.toast('닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.', 'error'); return; }
    await WOW.run(e.submitter, async function () {
      ctx.profile = await WOW.api.updateNickname(nick);
      renderProfile(ctx.profile);
      $('#nick-form').classList.add('hidden');
    }, '닉네임을 변경했습니다.');
  });

  // 비밀번호 변경
  $('#pw-form').addEventListener('submit', function (e) {
    e.preventDefault();
    const f = e.target;
    if (f.next.value !== f.confirm.value) { WOW.toast('새 비밀번호가 서로 다릅니다.', 'error'); return; }
    WOW.run(e.submitter, async function () {
      await WOW.api.changePassword(f.current.value, f.next.value);
      f.reset();
    }, '비밀번호를 변경했습니다. 다음 로그인부터 새 비밀번호를 사용하세요.');
  });

  // 덱
  $('#deck').innerHTML = ctx.deck.length === 0
    ? '<div class="warn">편성된 덱이 없습니다.</div>'
    : '<div class="deck-8">' + ctx.deck.map(function (id, i) {
        const k = WOW.getSkill(id);
        return '<div class="col gap-s" style="align-items:center">' + WOW.skillIcon(id, 52) +
          '<span class="display" style="font-size:11px;text-align:center;color:' + k.color + '">' + (i + 1) + '. ' + WOW.esc(k.name) + '</span></div>';
      }).join('') + '</div>';

  // 전적
  function renderMatches() {
    const filter = $('#mode-filter').value;
    const list = matches.filter(function (m) { return !filter || m.mode === filter; });
    const recent = matches.slice(0, 10);
    $('#analysis-title').textContent = '전적 분석 (최근 ' + matches.length + '경기)';
    $('#recent-wl').innerHTML = recent.length === 0 ? '<span class="muted">기록 없음</span>' : recent.map(function (m) {
      return '<span class="wl ' + (m.result === 'win' ? 'w' : 'l') + '" title="' + WOW.esc(WOW.MODE_LABEL[m.mode] + ' vs ' + m.opponent) + '">' + (m.result === 'win' ? 'W' : 'L') + '</span>';
    }).join('');
    if (matches.length) {
      const avgDur = matches.reduce(function (a, m) { return a + m.durationSec; }, 0) / matches.length;
      const avgCasts = matches.reduce(function (a, m) { return a + m.casts; }, 0) / matches.length;
      $('#avg-dur').textContent = WOW.fmtTime(avgDur);
      $('#avg-casts').textContent = Math.round(avgCasts);
    }
    $('#matches').innerHTML = list.length === 0
      ? '<div class="dim">아직 대전 기록이 없습니다. 첫 대전을 시작해보세요!</div>'
      : list.map(function (m) {
          const win = m.result === 'win';
          const d = m.ratingDelta;
          return '<div class="match-row">' +
            '<span class="display" style="font-weight:700;font-size:.85rem;letter-spacing:.1em;color:' + (win ? '#67e8f9' : '#fca5a5') + '">' + (win ? 'VICTORY' : 'DEFEAT') + '</span>' +
            '<span class="dim">' + WOW.MODE_LABEL[m.mode] + '</span>' +
            '<span class="truncate" style="color:var(--purple-100)">vs ' + WOW.esc(m.opponent) + '</span>' +
            '<span class="dim">' + WOW.fmtTime(m.durationSec) + '</span>' +
            '<span class="dim">' + m.casts + '회 시전</span>' +
            '<span style="font-weight:600;color:' + (d > 0 ? '#67e8f9' : d < 0 ? '#fca5a5' : '#6b6a8a') + '">' + (d > 0 ? '+' + d : d === 0 ? '±0' : d) + '</span>' +
            '<span class="muted text-right" style="font-size:.85rem">' + WOW.timeAgo(m.endedAt) + '</span></div>';
        }).join('');
  }
  $('#mode-filter').onchange = renderMatches;

  try {
    matches = await WOW.api.matches(ctx.profile.id, 30);
    renderMatches();
  } catch (e) {
    $('#matches').innerHTML = '<div class="err">' + WOW.esc(e.message) + '</div>';
  }
});
