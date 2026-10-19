// 랭킹전 매칭 대기 — 서버 대기열: 레이팅이 맞는 사람과 매칭, 15초가 지나면 봇
import { state, api, go, beginMatch, toast, icon, cardBackHTML, fmtTime } from "../core.js";
import { TIPS, RULES } from "../data.js";

export default {
  title: "매칭 대기",
  shell: false,
  render(root) {
    const started = Date.now();
    const tip = TIPS[Math.floor(Math.random() * TIPS.length)];
    let matched = null;
    let live = true;
    let polling = false;
    let lastPoll = 0;

    root.innerHTML = `<div class="table-bg on-vinyl"><div class="center-col">
      <div class="shuffle" aria-hidden="true">${cardBackHTML("md")}${cardBackHTML("md")}${cardBackHTML("md")}</div>
      <h1 class="big-title" id="q-title">상대를 찾고 있습니다</h1>
      <div class="timer" id="q-time" aria-live="off">0:00</div>
      <p id="q-sub" style="color:var(--on-vinyl-2)">레이팅 ${state.profile?.rating ?? "-"} ± 50 범위 탐색 중 · ${RULES.queueBotTimeout}초 넘으면 봇과 매칭</p>
      <p class="tip">${icon("spark", "sm")} ${tip}</p>
      <button class="btn" data-act="cancel">매칭 취소</button>
    </div></div>`;
    const $ = (id) => root.querySelector(id);

    const onMatched = (m) => {
      matched = m;
      clearInterval(timer);
      const isBot = m.bot !== false;
      $("#q-title").textContent = isBot ? "봇 매칭 완료" : "매칭 완료";
      $("#q-sub").textContent = isBot ? "대기 시간이 초과되어 봇과 매칭되었습니다." : `${m.opponentNickname} 님과 매칭되었습니다!`;
      $("#q-sub").style.color = "var(--live-on-vinyl)";
      root.querySelector('[data-act="cancel"]').disabled = true;
      setTimeout(() => {
        if (!live) return;
        beginMatch("ranked", { name: m.opponentNickname, rating: m.opponentRating ?? 1000, isBot, playerId: m.opponentPlayerId });
        go("countdown");
      }, 1600);
    };

    api.joinQueue().catch((e) => { if (live) { toast(e.message, "error"); go("lobby"); } });
    const timer = setInterval(() => {
      const t = (Date.now() - started) / 1000;
      $("#q-time").textContent = fmtTime(t);
      $("#q-sub").textContent = `레이팅 ${state.profile?.rating ?? "-"} ± ${50 + Math.floor(t) * 15} 범위 탐색 중 · ${RULES.queueBotTimeout}초 넘으면 봇과 매칭`;
      if (polling || Date.now() - lastPoll < 1000) return;
      polling = true;
      lastPoll = Date.now();
      api.queueStatus()
        .then((m) => { if (live && !matched && m?.state === "matched") onMatched(m); })
        .catch(() => {})
        .finally(() => { polling = false; });
    }, 250);

    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-act="cancel"]')) go("lobby");
    });
    return () => {
      live = false;
      clearInterval(timer);
      if (!matched) api.leaveQueue().catch(() => {});
    };
  },
};
