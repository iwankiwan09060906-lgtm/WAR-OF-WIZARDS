// 결과 — 승리/패배, 레이팅 변화, 기록(Unity 가 보낸 값이 있으면 그 값), 다시 대전 / 로비로
import { state, subscribe, go, beginMatch, icon, esc, fmtTime } from "../core.js";
import { MODE_LABEL } from "../data.js";

const STATS = [["casts", "시전 횟수", (v) => `${v}회`], ["minionsKilled", "처치한 유닛", (v) => Math.round(v)], ["damageDealt", "입힌 피해", (v) => Math.round(v)], ["damageTaken", "받은 피해", (v) => Math.round(v)]];
const TAGS = [["towerBroken", "적 타워 파괴"], ["nexusBroken", "적 넥서스 파괴"], ["towerLost", "아군 타워 손실"], ["nexusLost", "아군 넥서스 손실"]];

export default {
  title: "결과",
  shell: false,
  render(root) {
    const draw = () => {
      const m = state.lastMatch;
      if (!m) {
        root.innerHTML = `<div class="table-bg on-vinyl"><div class="center-col"><p class="big-title" style="font-size:1.8rem">표시할 대전 결과가 없습니다.</p><button class="btn btn-plate" data-act="lobby">로비로</button></div></div>`;
        return;
      }
      const win = m.result === "win";
      const rec = m.record;
      const s = m.stats || {};
      const delta = rec?.ratingDelta;
      root.innerHTML = `<div class="table-bg on-vinyl"><div class="center-col">
        <p style="color:var(--on-vinyl-2)">${MODE_LABEL[m.mode] || ""} · VS ${esc(m.opponent)}</p>
        <h1 class="result-title ${win ? "win" : "lose"}">${win ? "승리" : "패배"}</h1>
        ${m.reason ? `<p style="color:var(--on-vinyl-2);font-size:1.1rem">${esc(m.reason)}</p>` : ""}
        <div>${m.saveError ? `<span class="delta-chip down">전적 저장 실패: ${esc(m.saveError)}</span>`
          : !rec ? `<span class="delta-chip">전적 저장 중…</span>`
          : m.mode === "ranked" ? `<span class="delta-chip ${delta >= 0 ? "up" : "down"}">레이팅 ${delta >= 0 ? "+" : ""}${delta} → ${rec.newRating ?? state.profile?.rating}</span>`
          : `<span class="delta-chip">${MODE_LABEL[m.mode]}은 레이팅에 반영되지 않습니다. 전적은 저장되었습니다.</span>`}</div>
        <div class="result-stats">
          <div><b>${fmtTime(m.durationSec || 0)}</b><span>플레이 시간</span></div>
          ${STATS.map(([k, label, f]) => `<div><b>${s[k] != null ? f(s[k]) : "—"}</b><span>${label}</span></div>`).join("")}
        </div>
        ${TAGS.some(([k]) => s[k]) ? `<div style="display:flex;gap:8px;flex-wrap:wrap;justify-content:center">${TAGS.filter(([k]) => s[k]).map(([, l]) => `<span class="chip">${l}</span>`).join("")}</div>` : ""}
        <p class="hint" style="color:var(--on-vinyl-2)">처치 · 피해 기록은 Unity 게임이 결과를 보내면 채워집니다.</p>
        <div style="display:flex;gap:10px;flex-wrap:wrap;justify-content:center">
          <button class="btn btn-plate lg" data-act="again">${icon("swords")}${m.mode === "friendly" ? "룸으로 돌아가기" : "다시 대전"}</button>
          <button class="btn lg" data-act="lobby">로비로</button>
        </div>
      </div></div>`;
    };
    root.addEventListener("click", (e) => {
      const a = e.target.closest("[data-act]")?.dataset.act;
      if (a === "lobby") go("lobby");
      if (a === "again") {
        const m = state.lastMatch;
        if (m.mode === "ranked") go("queue");
        else if (m.mode === "friendly") go("room");
        else { beginMatch("bot", { name: "Bot", rating: state.profile?.rating ?? 1000, isBot: true }); go("countdown"); }
      }
    });
    draw();
    return subscribe((p) => { if ("lastMatch" in p || "profile" in p) draw(); });
  },
};
