// 404 — 없는 화면 주소
import { state, duelHTML } from "../core.js";

export default {
  title: "페이지를 찾을 수 없음",
  render(root) {
    root.innerHTML = `<div class="error-page"><div>
      <div class="error-code">4<span>0</span>4</div>
      ${duelHTML({ foe: false })}
      <h1 style="font-size:1.5rem;font-weight:900;margin-top:14px">마법진이 닿지 않는 곳입니다</h1>
      <p class="muted" style="margin-top:6px">주소가 바뀌었거나 없는 화면이에요. 로비로 돌아가 다시 시작하세요.</p>
      <div style="display:flex;gap:10px;justify-content:center;margin-top:22px">
        <a class="btn play lg" href="#/${state.user ? "lobby" : "login"}">${state.user ? "로비로" : "로그인 화면으로"}</a>
        <button class="btn ghost lg" onclick="history.back()">이전 화면</button>
      </div>
    </div></div>`;
  },
};
