// 랭킹 — 1~3위는 희귀 카드(홀로·금·은), 나머지는 표. 내 줄은 청록으로 표시
import { state, api, toast, playerCardHTML, tierGem, esc, winRate } from "../core.js";
import { tierOf } from "../data.js";

export default {
  title: "랭킹",
  shell: true,
  render(root) {
    let rows = null;

    const draw = () => {
      const meId = state.profile?.id;
      const mine = rows?.find((r) => r.userId === meId);
      const podium = (rows || []).slice(0, 3);
      root.innerHTML = `
        <header class="page-head">
          <div><h1 class="page-title">랭킹</h1><p class="page-lede">War of Wizards · 랭킹전 레이팅 기준 Top 100</p></div>
          <div class="head-actions">
            <span class="chip ${mine ? "live" : ""}">내 순위
              <b class="num">${mine ? `${mine.rank}위 · ${mine.rating}점 · ${mine.wins}승 ${mine.losses}패` : `Top 100 밖 · ${state.profile?.rating ?? "-"}점`}</b></span>
          </div>
        </header>
        ${rows === null ? `<p class="empty">불러오는 중…</p>` : `
          <div class="podium">
            ${podium.map((r, i) => `<div class="${["first", "second", "third"][i]}">
              ${playerCardHTML({ nickname: r.nickname, rating: r.rating, wins: r.wins, losses: r.losses },
                { label: `${i + 1}위`, frame: ["holo", "", "gold"][i] })}
            </div>`).join("")}
          </div>
          <section class="sheet" style="padding:8px 14px;overflow-x:auto">
            <table class="table">
              <thead><tr><th class="num">순위</th><th>닉네임</th><th class="hide-sm">등급</th><th class="num">레이팅</th><th class="num hide-sm">전적</th><th class="num">승률</th></tr></thead>
              <tbody>${rows.slice(3).map((r) => `
                <tr class="${r.userId === meId ? "me" : ""}">
                  <td class="num"><span class="place">${r.rank}</span></td>
                  <td><b>${esc(r.nickname)}</b>${r.userId === meId ? ` <span class="chip live" style="min-height:22px;font-size:.72rem">나</span>` : ""}</td>
                  <td class="hide-sm"><span style="display:inline-flex;align-items:center;gap:6px">${tierGem(r.rating, "sm")}${tierOf(r.rating).name}</span></td>
                  <td class="num"><b class="num">${r.rating}</b></td>
                  <td class="num hide-sm num">${r.wins}승 ${r.losses}패</td>
                  <td class="num num">${winRate(r.wins, r.losses)}%</td>
                </tr>`).join("")}
              </tbody>
            </table>
          </section>`}`;
    };

    draw();
    api.getRanking(100).then((r) => { rows = r; draw(); }).catch((e) => { rows = []; draw(); toast(e.message, "error"); });
  },
};
