// 13. 결과 — VICTORY / DEFEAT, 이유, 기록, 레이팅 변화, 다시 대전 · 로비로
import { state, subscribe, go, beginMatch, icon, esc, mmss, MODE, duelHTML } from "../core.js";

const STATS = [["casts", "시전 횟수", (v) => `${v}회`], ["minionsKilled", "처치한 유닛", Math.round], ["damageDealt", "입힌 피해", Math.round], ["damageTaken", "받은 피해", Math.round]];

export default {
  title: "결과",
  render(root) {
    const draw = () => {
      const m = state.lastMatch;
      if (!m) {
        root.innerHTML = `<div class="focus-screen"><p class="muted">표시할 대전 결과가 없습니다.</p><a class="btn play" href="#/lobby">로비로</a></div>`;
        return;
      }
      const win = m.result === "win";
      const rec = m.record;
      const s = m.stats || {};
      const d = rec?.ratingDelta;
      const shown = STATS.filter(([k]) => s[k] != null);
      root.innerHTML = `<div class="focus-screen">
        <div style="height:min(24vh,200px);width:min(440px,90vw)">${duelHTML({ st: win ? "win" : "lose" })}</div>
        <h1 class="result-word ${win ? "win" : "lose"}">${win ? "VICTORY" : m.result === "draw" ? "DRAW" : "DEFEAT"}</h1>
        <p class="subline">${MODE[m.mode] || ""} · ${esc(m.opponent?.name)} 상대</p>
        ${m.reason ? `<p class="muted">${esc(m.reason)}</p>` : ""}
        <div>${m.saveError ? `<span class="chip red">전적 저장 실패: ${esc(m.saveError)}</span>`
          : !rec ? `<span class="chip">전적 저장 중…</span>`
          : m.mode === "ranked" ? `<span class="chip ${d >= 0 ? "cyan" : "red"} tech" style="font-size:1.05rem;min-height:38px">레이팅 ${d >= 0 ? "+" : ""}${d}${rec.newRating ? ` → ${rec.newRating}` : ""}</span>`
          : `<span class="chip">${MODE[m.mode]}은 레이팅에 반영되지 않습니다. 전적은 저장되었습니다.</span>`}</div>
        <div class="stat-row" style="--n:${1 + shown.length}">
          <div><b>${mmss(m.durationSec || 0)}</b><span>플레이 시간</span></div>
          ${shown.map(([k, label, f]) => `<div><b>${f(s[k])}</b><span>${label}</span></div>`).join("")}
        </div>
        ${shown.length < STATS.length ? `<p class="hint">처치 · 피해 기록은 Unity 게임이 결과를 보내면 함께 표시됩니다.</p>` : ""}
        <div style="display:flex;gap:10px;flex-wrap:wrap;justify-content:center">
          <button class="btn play lg" data-act="again">${icon("play")}${m.mode === "friendly" ? "룸으로 돌아가기" : "다시 대전"}</button>
          <a class="btn lg" href="#/lobby">로비로</a>
        </div>
      </div>`;
    };
    root.addEventListener("click", (e) => {
      if (!e.target.closest('[data-act="again"]')) return;
      const m = state.lastMatch;
      if (m.mode === "ranked") { beginMatch("ranked", { name: "탐색 중…", rating: 0, bot: false }); go("queue"); }
      else if (m.mode === "friendly") go("room");
      else { beginMatch("bot", { name: "Bot", rating: state.profile.rating, bot: true }); go("countdown"); }
    });
    draw();
    return subscribe((p) => { if ("lastMatch" in p) draw(); });
  },
};
