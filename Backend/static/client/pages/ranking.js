// 8. 랭킹 — 내 순위 + Top 100 표 (1~3위 강조)
import { api, state, toast, esc, headerHTML, tierOf, winRate } from "../core.js";

export default {
  title: "랭킹",
  render(root) {
    let rows = null;
    const draw = () => {
      const meId = state.profile?.id;
      const mine = rows?.find((r) => r.userId === meId);
      root.innerHTML = `${headerHTML("랭킹", { right: `<span class="chip ${mine ? "cyan" : ""}">내 순위 <b class="tech">${mine ? `${mine.rank}위 · ${mine.rating}점 · ${mine.wins}승 ${mine.losses}패` : `Top 100 밖 · ${state.profile?.rating ?? "-"}점`}</b></span>` })}
        <div class="body"><div class="wrap" style="max-width:1100px">
          <p class="muted" style="margin-bottom:14px">랭킹전 레이팅 기준 · 상위 100명</p>
          <section class="card">${rows === null ? `<p class="empty">불러오는 중…</p>` : `<div class="table-wrap"><table class="table">
            <thead><tr><th style="width:96px">순위</th><th>닉네임</th><th class="hide-sm">등급</th><th class="r">레이팅</th><th class="r hide-sm">전적</th><th class="r">승률</th></tr></thead>
            <tbody>${rows.map((r) => `<tr class="${r.userId === meId ? "is-me" : ""}">
              <td><span class="rank-no ${r.rank <= 3 ? `rank-${r.rank}` : ""}">${r.rank}</span></td>
              <td><b>${esc(r.nickname)}</b>${r.userId === meId ? ` <span class="chip cyan" style="min-height:20px;font-size:.7rem">나</span>` : ""}</td>
              <td class="hide-sm muted">${tierOf(r.rating)}</td>
              <td class="r num"><b>${r.rating}</b></td>
              <td class="r hide-sm muted">${r.wins}승 ${r.losses}패</td>
              <td class="r num">${winRate(r.wins, r.losses)}%</td></tr>`).join("")}</tbody></table></div>`}
          </section>
        </div></div>`;
    };
    draw();
    api.getRanking(100).then((r) => { rows = r; draw(); }).catch((e) => { rows = []; draw(); toast(e.message, "error"); });
  },
};
