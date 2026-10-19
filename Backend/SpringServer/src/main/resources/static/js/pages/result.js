// 캔버스 13 · 결과 화면
// 전투 화면에서 저장에 실패했으면 여기서 한 번 더 저장한다. (matchToken 이 같아서 중복 기록되지 않음)
WOW.page({ auth: true }, async function (ctx) {
  WOW.decorate();
  const $ = WOW.$;
  const m = WOW.state.getLastMatch();

  if (!m) { $('#empty').classList.remove('hidden'); return; }
  $('#view').classList.remove('hidden');

  const win = m.result === 'win';
  const accent = win ? '#67e8f9' : '#f87171';
  $('#bg').className = 'bg ' + (win ? 'bg-purple' : 'bg-red');
  $('#sub').textContent = WOW.MODE_LABEL[m.mode] + ' · vs ' + m.opponent;
  $('#title').textContent = win ? 'VICTORY' : 'DEFEAT';
  $('#title').style.color = accent;
  $('#title').style.textShadow = '0 0 50px ' + accent;
  $('#reason').textContent = m.reason;

  const stat = function (label, value, color) {
    return '<div class="result-stat"><b style="color:' + (color || '#e8e4ff') + '">' + value + '</b><span>' + label + '</span></div>';
  };
  $('#stats').innerHTML =
    stat('플레이 시간', WOW.fmtTime(m.durationSec), '#22d3ee') +
    stat('시전 횟수', m.stats.casts) +
    stat('처치한 유닛', Math.round(m.stats.minionsKilled), '#fb923c') +
    stat('입힌 피해', Math.round(m.stats.damageDealt), '#f87171') +
    stat('받은 피해', Math.round(m.stats.damageTaken), '#a78bfa');

  const badge = function (on, text) { return '<span class="result-badge' + (on ? ' on' : '') + '">' + (on ? '✦ ' : '') + text + '</span>'; };
  $('#badges').innerHTML =
    badge(m.stats.towerDestroyed, '적 타워 파괴') + badge(m.stats.nexusDestroyed, '적 넥서스 파괴') +
    badge(m.stats.ownTowerLost, '아군 타워 손실') + badge(m.stats.ownNexusLost, '아군 넥서스 손실');

  async function showSave() {
    const box = $('#save');
    if (!m.record) {
      box.innerHTML = '<span class="err">전적 저장 실패: ' + WOW.esc(m.saveError || '알 수 없는 오류') + '</span> <button class="btn btn-ghost btn-xs" id="btn-retry">다시 저장</button>';
      $('#btn-retry').onclick = function () { WOW.run(this, save); };
      return;
    }
    if (m.mode === 'ranked') {
      const d = m.record.ratingDelta;
      const profile = await WOW.loadProfile();
      box.innerHTML = '레이팅 <b style="color:' + (d >= 0 ? '#67e8f9' : '#fca5a5') + '">' + (d >= 0 ? '+' : '') + d + '</b>' +
        '<span style="color:var(--purple-300)"> → ' + profile.rating + '</span>';
    } else {
      box.innerHTML = '<span style="color:var(--purple-300)">' + WOW.MODE_LABEL[m.mode] + '은 레이팅에 반영되지 않습니다. 전적은 저장되었습니다.</span>';
    }
  }

  async function save() {
    try {
      m.record = await WOW.api.recordMatch({
        mode: m.mode, result: m.result, opponent: m.opponent, durationSec: m.durationSec,
        casts: m.stats.casts, damageDealt: m.stats.damageDealt, damageTaken: m.stats.damageTaken, matchToken: m.token,
      });
      m.saveError = null;
    } catch (e) {
      m.saveError = e.message;
    }
    WOW.state.setLastMatch(m);
    await showSave();
  }

  if (!m.record) await save(); else await showSave();

  // 다시 대전: 같은 모드로
  const again = $('#btn-again');
  if (m.mode === 'friendly') again.textContent = '새 친선전 룸';
  again.onclick = function () {
    if (m.mode === 'ranked') { WOW.beginMatch('ranked', { name: '탐색 중…', rating: 0, isBot: false }); WOW.go('queue'); }
    else if (m.mode === 'friendly') WOW.go('room');
    else { WOW.beginMatch('bot', { name: 'Bot', rating: ctx.profile.rating, isBot: true }); WOW.go('countdown'); }
  };
});
