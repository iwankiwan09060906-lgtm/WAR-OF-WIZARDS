// 마이페이지 — 내 마법사 카드 · 전적 분석 · 내 덱(HUD 순서) · 최근 대전
import { state, api, refreshProfile, subscribe, playerCardHTML, cardHTML, icon, esc, fmtTime, timeAgo, winRate } from "../core.js";
import { MODE_LABEL } from "../data.js";

export default {
  title: "마이페이지",
  shell: true,
  render(root) {
    let matches = null;
    let error = "";

    const draw = () => {
      const p = state.profile;
      const recent = (matches || []).slice(0, 10);
      const n = matches?.length || 0;
      const avgDur = n ? matches.reduce((a, m) => a + (m.durationSec || 0), 0) / n : 0;
      const avgCasts = n ? matches.reduce((a, m) => a + (m.casts || 0), 0) / n : 0;
      const recentWins = (matches || []).filter((m) => m.result === "win").length;
      root.innerHTML = `
        <header class="page-head"><div><h1 class="page-title">마이페이지</h1><p class="page-lede">내 마법사 카드와 최근 대전 기록입니다.</p></div></header>
        <div class="mine">
          <div class="side-stack">
            ${playerCardHTML(p, { label: "MY WIZARD" })}
            <section class="sheet">
              <h2 class="section-title">전적 분석 <span class="count">최근 ${n}경기</span></h2>
              ${n ? `
                <div class="facts">
                  <div><b>${winRate(recentWins, n - recentWins)}%</b><span>최근 승률</span></div>
                  <div><b>${fmtTime(avgDur)}</b><span>평균 플레이 시간</span></div>
                  <div><b>${avgCasts.toFixed(1)}</b><span>평균 시전 횟수</span></div>
                </div>
                <div class="streak" style="margin-top:14px" aria-label="최근 경기 결과">${(matches || []).slice(0, 20).reverse().map((m) => `<i class="${m.result === "win" ? "w" : "l"}" title="${m.result === "win" ? "승" : "패"}">${m.result === "win" ? "승" : "패"}</i>`).join("")}</div>
                <p class="hint" style="margin-top:6px">왼쪽이 오래된 경기, 오른쪽이 최근 경기</p>` : `<p class="empty">기록 없음</p>`}
            </section>
          </div>
          <div>
            <section class="sheet">
              <div style="display:flex;justify-content:space-between;align-items:baseline;gap:10px">
                <h2 class="section-title">내 덱 <span class="count">HUD 슬롯 순서</span></h2>
                <a class="btn btn-quiet sm" href="#/spellbook">덱 편성 ${icon("arrow", "sm")}</a>
              </div>
              ${state.deck.length ? `<div class="deck-strip" style="margin-top:0"><div class="sleeve">${state.deck.map((id, i) => `<div class="pocket"><span class="slot-no">${i + 1}</span>${cardHTML(id, { size: "sm", flip: "hover" })}</div>`).join("")}</div></div>`
                : `<p class="empty">편성된 덱이 없습니다.</p>`}
            </section>
            <section class="sheet" style="overflow-x:auto">
              <h2 class="section-title">최근 대전</h2>
              ${error ? `<div class="notice-box">${esc(error)}</div>` : matches === null ? `<p class="empty">불러오는 중…</p>` : recent.length === 0
                ? `<p class="empty">아직 대전 기록이 없습니다. 첫 대전을 시작해보세요!</p>`
                : `<table class="table"><thead><tr><th>결과</th><th>모드</th><th>상대</th><th class="num hide-sm">시간</th><th class="num hide-sm">시전</th><th class="num">레이팅</th><th class="num hide-sm">언제</th></tr></thead><tbody>
                  ${recent.map((m) => `<tr>
                    <td><span class="chip ${m.result === "win" ? "live" : "warn"}">${m.result === "win" ? "승리" : m.result === "draw" ? "무승부" : "패배"}</span></td>
                    <td>${MODE_LABEL[m.mode] || esc(m.mode)}</td>
                    <td><b>${esc(m.opponent)}</b></td>
                    <td class="num hide-sm num">${fmtTime(m.durationSec)}</td>
                    <td class="num hide-sm num">${m.casts}회</td>
                    <td class="num num ${m.ratingDelta > 0 ? "delta-up" : m.ratingDelta < 0 ? "delta-down" : ""}">${m.ratingDelta > 0 ? "+" : ""}${m.ratingDelta || 0}</td>
                    <td class="num hide-sm hint">${timeAgo(m.endedAt)}</td></tr>`).join("")}
                  </tbody></table>`}
            </section>
          </div>
        </div>`;
    };

    draw();
    refreshProfile().catch(() => {});
    api.listMatches(state.user.id, 30).then((m) => { matches = m; draw(); }).catch((e) => { error = e.message; draw(); });
    return subscribe((p) => { if ("profile" in p || "deck" in p) draw(); });
  },
};
