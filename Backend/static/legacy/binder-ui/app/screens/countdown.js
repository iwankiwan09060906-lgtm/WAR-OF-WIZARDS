// 카운트다운 — 나와 상대의 마법사 카드가 마주 보고 3 · 2 · 1 → 게임 화면
import { state, go, playerCardHTML, cardHTML, cardBackHTML } from "../core.js";
import { RULES, MODE_LABEL } from "../data.js";

export default {
  title: "대전 준비",
  shell: false,
  render(root) {
    const opp = state.opponent;
    if (!opp) { go("lobby"); return; }
    const me = state.profile;
    root.innerHTML = `<div class="table-bg on-vinyl"><div class="vs">
      <div class="vs-side me">
        ${playerCardHTML(me, { label: "나" })}
        <div class="vs-hand">${state.deck.map((id) => cardHTML(id, { size: "xs" })).join("")}</div>
      </div>
      <div style="text-align:center">
        <div class="vs-count num" id="count" aria-live="assertive">${RULES.countdown}</div>
        <div class="big-title" style="font-size:1.6rem;margin-top:8px">VS</div>
        <div style="color:var(--on-vinyl-2);margin-top:6px">${MODE_LABEL[state.matchMode] || ""}</div>
      </div>
      <div class="vs-side">
        ${playerCardHTML({ nickname: opp.name, rating: opp.rating }, { label: opp.isBot ? "상대 · Bot" : "상대", compact: true })}
        <div class="vs-hand">${Array.from({ length: 8 }, () => cardBackHTML("xs")).join("")}</div>
      </div>
      <p class="vs-foot">손이 카메라에 잘 보이도록 두 손을 앞으로 내밀어 주세요.</p>
    </div></div>`;
    let n = RULES.countdown;
    const el = root.querySelector("#count");
    const tick = () => {
      n -= 1;
      if (n < 0) return go("game");
      el.classList.toggle("go", n === 0);
      el.textContent = n > 0 ? String(n) : "START!";
      el.style.animation = "none";
      void el.offsetWidth;
      el.style.animation = "";
      timer = setTimeout(tick, n === 0 ? 700 : 1000);
    };
    let timer = setTimeout(tick, 1000);
    return () => clearTimeout(timer);
  },
};
