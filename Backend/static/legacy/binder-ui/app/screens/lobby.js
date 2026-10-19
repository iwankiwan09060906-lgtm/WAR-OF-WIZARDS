// 로비 — 대전 시작 판(반전 판은 '랭킹전 매칭' 하나) + 내 덱 슬리브 + 마법사 카드
import { state, api, subscribe, go, beginMatch, refreshMails, icon, cardHTML, playerCardHTML, unread, esc, winRate } from "../core.js";
import { deckProblem, RULES } from "../data.js";

export default {
  title: "로비",
  shell: true,
  render(root) {
    let onlineFriends = null;

    const draw = () => {
      const p = state.profile;
      const problem = deckProblem(state.deck);
      const n = unread();
      const notice = state.mails.find((m) => m.kind === "notice");
      root.innerHTML = `
        <header class="page-head">
          <div><h1 class="page-title">로비</h1><p class="page-lede">덱 8장을 들고 대결을 시작하세요. 전투는 Unity 게임 화면에서 진행됩니다.</p></div>
        </header>
        <div class="lobby">
          <div>
            <section class="duel on-vinyl" aria-labelledby="duel-h">
              <img class="duel-art" src="assets/skills/circle/H02.png" alt="">
              <h2 id="duel-h">1 : 1 마법 대전</h2>
              <p>룬을 그려 마법을 시전하고, 상대 넥서스를 파괴해 돌파하세요.</p>
              <div class="duel-actions">
                <button class="btn btn-plate" data-act="ranked" ${problem ? "disabled" : ""}>랭킹전 매칭<span class="plate-sub">레이팅 ${p?.rating ?? "-"} · ${RULES.queueBotTimeout}초 넘으면 봇과 대결</span></button>
                <button class="btn" data-act="friendly">${icon("door")}친선전 룸</button>
                <button class="btn" data-act="bot" ${problem ? "disabled" : ""}>${icon("bot")}봇 연습전</button>
              </div>
            </section>
            <section class="sheet deck-strip" aria-labelledby="deck-h">
              <div style="display:flex;justify-content:space-between;align-items:baseline;gap:12px">
                <h2 class="section-title" id="deck-h">내 마법서 <span class="count">${state.deck.length}/${RULES.deckSize}</span></h2>
                <a class="btn btn-quiet sm" href="#/spellbook">편성하기 ${icon("arrow", "sm")}</a>
              </div>
              ${problem && state.deck.length === 0
                ? `<div class="notice-box info">${icon("book")}<span>아직 덱이 없습니다. 마법서에서 스킬 8장을 편성해야 대전을 시작할 수 있어요.</span></div>`
                : `${problem ? `<div class="notice-box" style="margin-bottom:12px">${esc(problem)}</div>` : ""}
                   <div class="sleeve">${state.deck.map((id, i) => `<div class="pocket"><span class="slot-no">${i + 1}</span>${cardHTML(id, { flip: "hover", size: "sm" })}</div>`).join("")}</div>
                   <p class="hint" style="margin-top:10px">카드에 마우스를 올리면 그려야 할 룬이 마법진으로 뒤집힙니다.</p>`}
            </section>
          </div>
          <div class="side-stack">
            <a href="#/mypage" style="text-decoration:none" aria-label="마이페이지">${playerCardHTML(p, { label: "MY WIZARD" })}</a>
            <nav class="sheet quick" aria-label="바로가기" style="padding:6px 18px">
              <div class="list">
                <a class="row row-link" href="#/friends">${icon("users")}<span class="grow"><span class="name">친구</span><br><span class="sub">${onlineFriends === null ? "확인 중…" : `접속 중 ${onlineFriends}명`}</span></span>${icon("right", "sm")}</a>
                <a class="row row-link" href="#/mailbox">${icon("mail")}<span class="grow"><span class="name">우편함 · 공지</span><br><span class="sub">${n ? `읽지 않음 ${n}건` : "새 우편 없음"}</span></span>${n ? `<span class="badge">${n}</span>` : icon("right", "sm")}</a>
                ${notice ? `<a class="row row-link" href="#/mailbox">${icon("bell")}<span class="grow"><span class="name">${esc(notice.title)}</span><br><span class="sub">최근 공지</span></span>${icon("right", "sm")}</a>` : ""}
                <a class="row row-link" href="#/ranking">${icon("trophy")}<span class="grow"><span class="name">랭킹</span><br><span class="sub">랭킹전 레이팅 기준 Top 100</span></span>${icon("right", "sm")}</a>
                <a class="row row-link" href="#/mypage">${icon("user")}<span class="grow"><span class="name">마이페이지</span><br><span class="sub">${p ? `${p.wins}승 ${p.losses}패 · 승률 ${winRate(p.wins, p.losses)}%` : ""}</span></span>${icon("right", "sm")}</a>
                <a class="row row-link" href="#/settings">${icon("gear")}<span class="grow"><span class="name">환경설정</span><br><span class="sub">볼륨 · 비네팅 · 제스처</span></span>${icon("right", "sm")}</a>
              </div>
            </nav>
          </div>
        </div>`;
    };

    const countFriends = () => {
      if (!state.user) return;
      api.listFriends(state.user.id)
        .then((list) => {
          onlineFriends = list.filter((f) => f.relation === "friend" && state.online.includes(f.id)).length;
          draw();
        })
        .catch(() => {});
    };

    root.addEventListener("click", (e) => {
      const act = e.target.closest("[data-act]")?.dataset.act;
      if (act === "ranked") go("queue");
      if (act === "friendly") go("room");
      if (act === "bot") {
        beginMatch("bot", { name: "Bot", rating: state.profile?.rating ?? 1000, isBot: true });
        go("countdown");
      }
    });

    draw();
    countFriends();
    refreshMails().catch(() => {});
    const unsub = subscribe((patch) => {
      if ("online" in patch) countFriends();
      if ("profile" in patch || "deck" in patch || "mails" in patch) draw();
    });
    return unsub;
  },
};
