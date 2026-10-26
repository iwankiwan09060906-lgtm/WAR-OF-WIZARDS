// 10. 매칭 대기 — 파이썬 서버 대기열: 레이팅이 맞는 사람과 매칭, 15초가 지나면 봇. 매칭되면 서버가 게임 방을 만든다.
//     3 · 4인 개인전은 그 인원이 모이면 바로, 시간이 지나면 모인 사람만으로 시작하고 빈자리는 봇.
import { api, state, go, beginServerMatch, toast, icon, mmss, TIPS, RULES, duelHTML } from "../core.js";

export default {
  title: "매칭 대기",
  render(root) {
    const started = Date.now();
    const tip = TIPS[Math.floor(Math.random() * TIPS.length)];
    let matched = null;
    let live = true;
    let polling = false;
    let lastPoll = 0;
    const rating = state.profile.rating;
    const need = [3, 4].includes(state.settings.players) ? state.settings.players : 2;
    const searching = (s) => need > 2
      ? `${need}인 개인전 · 마법사 ${s?.playersFound ?? 1}/${need}명 · 레이팅 ${rating} ± ${s?.ratingWindow ?? 50} · ${RULES.botTimeout}초 지나면 빈자리는 봇`
      : `레이팅 ${rating} ± ${s?.ratingWindow ?? 50} 범위 탐색 중 · 대기 ${s?.queueSize ?? 1}명 · ${RULES.botTimeout}초 초과 시 봇 매칭`;

    root.innerHTML = `<div class="focus-screen">
      <h1 id="q-title">${need > 2 ? `${need}인 개인전 상대를 찾고 있습니다` : "상대를 찾고 있습니다"}</h1>
      <div style="display:flex;align-items:center;gap:clamp(10px,3vw,40px)">
        <div style="height:min(30vh,240px)">${duelHTML({ foe: false, st: "search" })}</div>
      </div>
      <div class="big-timer" id="q-time">0:00</div>
      <div class="progress" aria-hidden="true"><i id="q-bar"></i></div>
      <p class="muted" id="q-sub">${searching(null)}</p>
      <p class="tip">${icon("info")}<span>${tip}</span></p>
      <button class="btn ghost" data-act="cancel">${icon("x")}매칭 취소</button>
    </div>`;
    const $ = (s) => root.querySelector(s);

    const onMatched = (m) => {
      matched = m;
      const bot = m.bot !== false;
      $("#q-title").textContent = bot ? "봇 매칭 완료" : "매칭 완료!";
      $("#q-sub").textContent = bot ? "대기 시간이 초과되어 봇과 매칭되었습니다."
        : need > 2 ? `상대 ${m.opponentNickname} (빈자리는 봇)` : `${m.opponentNickname} 님과 매칭되었습니다!`;
      $("#q-sub").className = "cyan";
      $(".wiz.foe").classList.remove("unknown");
      root.querySelector('[data-act="cancel"]').disabled = true;
      beginServerMatch("ranked", m);
      setTimeout(() => { if (live) go("countdown"); }, 1500);
    };

    api.joinQueue(need).catch((e) => { if (live) { toast(e.message, "error"); go("lobby"); } });
    const timer = setInterval(() => {
      const t = (Date.now() - started) / 1000;
      if (matched) return;
      $("#q-time").textContent = mmss(t);
      $("#q-bar").style.setProperty("--p", String(Math.min(1, t / RULES.botTimeout)));
      if (polling || Date.now() - lastPoll < 1000) return;
      polling = true;
      lastPoll = Date.now();
      api.queueStatus().then((s) => {
        if (!live || matched) return;
        if (s.state === "matched") onMatched(s);
        else if (s.state === "searching") $("#q-sub").textContent = searching(s);
        else if (s.state === "none") api.joinQueue(need).catch(() => {});
      }).catch(() => {}).finally(() => { polling = false; });
    }, 250);

    root.addEventListener("click", (e) => { if (e.target.closest('[data-act="cancel"]')) go("lobby"); });
    return () => { live = false; clearInterval(timer); if (!matched) api.leaveQueue().catch(() => {}); };
  },
};
