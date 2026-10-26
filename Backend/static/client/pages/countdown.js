// 11. 카운트다운 — 나 (덱 8장) · 3 · 2 · 1 · VS · 상대 (덱은 가려짐) → 전투 HUD
import { state, go, esc, wizardSVG, skHTML, tierOf, MODE, RULES } from "../core.js";

export default {
  title: "대전 준비",
  render(root) {
    const ctx = state.matchCtx;
    if (!ctx) { go("lobby"); return; }
    const me = state.profile;
    const opp = ctx.opponent;
    root.innerHTML = `<div class="vs-screen">
      <div class="vs-side"><span class="side-label cyan">YOU</span><div class="fig wiz">${wizardSVG("you")}</div>
        <div class="who">${esc(me.nickname)}</div><div class="meta">${tierOf(me.rating)} · <span class="num">${me.rating}</span></div>
        <div class="vs-hand">${state.deck.map((id) => skHTML(id, 34)).join("")}</div></div>
      <div style="text-align:center"><div class="count-num" id="count" aria-live="assertive">${RULES.countdown}</div>
        <div class="muted" style="margin-top:6px">${MODE[ctx.mode] || ""}${ctx.maxPlayers > 2 ? ` · ${ctx.maxPlayers}인 개인전` : ""}</div>${ctx.roomCode ? `<div class="hint" style="margin-top:6px">게임 방 ${esc(ctx.roomCode)}</div>` : ""}</div>
      <div class="vs-side"><span class="side-label red">ENEMY${ctx.maxPlayers > 2 ? ` ×${ctx.maxPlayers - 1}` : ""}${opp.bot ? " · BOT" : ""}</span><div class="fig wiz">${wizardSVG("foe")}</div>
        <div class="who">${esc(opp.name)}</div><div class="meta">${opp.rating ? `${tierOf(opp.rating)} · <span class="num">${opp.rating}</span>` : ""}</div>
        <div class="vs-hand">${Array.from({ length: 8 }, () => `<span class="cover">?</span>`).join("")}</div></div>
    </div>
    <p class="hint" style="position:fixed;left:0;right:0;bottom:22px;text-align:center">손이 카메라에 잘 보이도록 두 손을 앞으로 내밀어 주세요.</p>`;
    let n = RULES.countdown;
    const el = root.querySelector("#count");
    let timer;
    const tick = () => {
      n -= 1;
      if (n < 0) return go("battle");
      el.classList.toggle("go", n === 0);
      el.textContent = n > 0 ? String(n) : "START!";
      el.style.animation = "none"; void el.offsetWidth; el.style.animation = "";
      timer = setTimeout(tick, n === 0 ? 700 : 1000);
    };
    timer = setTimeout(tick, 1000);
    return () => clearTimeout(timer);
  },
};
