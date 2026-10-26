// 2. 메인 로비 — 왼쪽: 마법 대전 카드(마법사 1:1 그림 · 게임 방법 · 1:1 / 3 · 4인 개인전 선택 · 매칭 버튼 · 내 마법서), 오른쪽: 메뉴 타일 6개
import { api, state, subscribe, go, beginMatch, beginServerMatch, updateSettings, refreshMails, signOut, toast, icon, esc, headerHTML, duelHTML, skHTML, deckProblem, tierOf, winRate, unread, isAdmin } from "../core.js";

export default {
  title: "메인 로비",
  render(root) {
    let onlineFriends = null;

    const draw = () => {
      const p = state.profile;
      const problem = deckProblem(state.deck);
      const n = unread();
      const size = [3, 4].includes(state.settings.players) ? state.settings.players : 2;
      root.innerHTML = `
        ${headerHTML("메인 로비", { back: false, right: `
          <div style="text-align:right;line-height:1.3"><b style="font-size:1.05rem">${esc(p.nickname)}</b><br><span class="tech cyan" style="font-size:.85rem">${tierOf(p.rating)} · ${p.rating}</span></div>
          <button class="btn ghost sm" data-act="logout">${icon("logout")}로그아웃</button>` })}
        <div class="body"><div class="wrap lobby">
          <section class="card battle-card" aria-labelledby="battle-h">
            <div class="hero">${duelHTML({ foe: false })}</div>
            <div class="battle-body">
              <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px;flex-wrap:wrap">
                <div><h2 id="battle-h">${size > 2 ? `${size}인 개인전` : "1 : 1 마법 대전"}</h2><p style="margin-top:6px">${size > 2 ? `팀 없이 ${size}명이 겨룹니다. 나 빼고 모두 적 — 마지막까지 남으세요. 빈자리는 봇이 채웁니다.` : "룬을 그려 마법을 쓰고, 유닛을 밀어 상대 넥서스를 무너뜨리세요."}</p></div>
                <div class="lanes size" role="group" aria-label="대전 인원">${[2, 3, 4].map((k) => `<button data-size="${k}" aria-pressed="${k === size}">${k > 2 ? `${k}인` : "1 : 1"}</button>`).join("")}</div>
              </div>
              <ol class="how" aria-label="게임 방법">
                <li class="on"><span class="n">01</span><b>룬 그리기</b><span>핀치한 채 허공에</span></li>
                <li><span class="n">02</span><b>준비</b><span>주먹을 쥔다</span></li>
                <li><span class="n">03</span><b>조준</b><span>검지로 열 지정</span></li>
                <li><span class="n">04</span><b>발동</b><span>마법진이 터진다</span></li>
              </ol>
              <div class="battle-actions">
                <button class="btn play xl" data-act="ranked" ${problem ? "disabled" : ""}>${icon("play")}${size > 2 ? `${size}인 개인전 매칭` : "랭킹전 매칭"}</button>
                <button class="btn info" data-act="friendly">${icon("door")}친선전</button>
                <button class="btn ghost" data-act="bot" ${problem ? "disabled" : ""}>${icon("bot")}봇 연습전</button>
              </div>
              <div class="deck-strip">
                <div style="display:flex;justify-content:space-between;align-items:center"><span class="label">내 마법서</span><a class="btn text" href="#/spellbook">편성하기 ${icon("arrow")}</a></div>
                ${state.deck.length === 0
                  ? `<p class="amber" style="margin-top:8px">아직 덱이 없습니다. 마법서에서 스킬 8장을 편성해야 대전을 시작할 수 있어요.</p>`
                  : problem ? `<p class="amber" style="margin-top:8px">${esc(problem)}</p>`
                  : `<div class="icons">${state.deck.map((id) => skHTML(id, 46)).join("")}</div>`}
              </div>
            </div>
          </section>
          <div class="stack">
            <nav class="tiles" aria-label="메뉴">
              <a class="card tile" href="#/spellbook">${icon("book")}<b>마법서</b><span>19종 중 8장 덱 편성</span></a>
              <a class="card tile" href="#/mypage">${icon("user")}<b>마이페이지</b><span>${p.wins}승 ${p.losses}패 · 승률 ${winRate(p.wins, p.losses)}%</span></a>
              <a class="card tile" href="#/friends">${icon("users")}<b>친구</b><span>${onlineFriends === null ? "확인 중…" : `접속 중 ${onlineFriends}명`}</span></a>
              <a class="card tile" href="#/mailbox">${icon("mail")}<b>우편함 · 공지</b><span>${n ? `읽지 않음 ${n}건` : "새 우편 없음"}</span>${n ? `<span class="badge">${n}</span>` : ""}</a>
              <a class="card tile" href="#/ranking">${icon("trophy")}<b>랭킹</b><span>랭킹전 레이팅 기준</span></a>
              <a class="card tile" href="#/settings">${icon("gear")}<b>환경설정</b><span>볼륨 · 비네팅 · 제스처</span></a>
            </nav>
            <div class="card card-pad" style="display:flex;align-items:center;gap:14px">
              ${icon("pen", "lg")}<div style="flex:1"><b>룬 연습장</b><div class="hint">마우스로 룬을 그려 인식 · Unity 시전 신호를 시험합니다</div></div>
              <a class="btn info sm" href="#/runes">열기</a>${isAdmin() ? `<a class="btn ghost sm" href="#/admin">관리자</a>` : ""}
            </div>
          </div>
        </div></div>`;
    };

    const countFriends = () => api.listFriends(state.user.id)
      .then((l) => { onlineFriends = l.filter((f) => f.relation === "friend" && state.online.includes(f.id)).length; draw(); })
      .catch(() => {});

    root.addEventListener("click", (e) => {
      const sz = e.target.closest("[data-size]")?.dataset.size;
      if (sz) { updateSettings({ players: Number(sz) }); draw(); return; }
      const a = e.target.closest("[data-act]")?.dataset.act;
      if (!a) return;
      const problem = deckProblem(state.deck);
      if (a === "logout") signOut();
      if (a === "friendly") go("room");
      if ((a === "ranked" || a === "bot") && problem) return toast(problem, "error");
      if (a === "ranked") { beginMatch("ranked", { name: "탐색 중…", rating: 0, bot: false }); go("queue"); }
      if (a === "bot") { beginMatch("bot", { name: "Bot", rating: state.profile.rating, bot: true }); go("countdown"); }   // 봇 연습은 1:1
    });

    draw();
    countFriends();
    refreshMails().catch(() => {});
    // 끝나지 않은 게임 방(랭킹전 · 개인전 친선전)이 있으면 이어서
    api.currentMatch().then((m) => {
      if (m && (m.status === "MATCHED" || m.status === "PLAYING")) {
        const mode = m.mode === "friendly" ? "friendly" : "ranked";
        beginServerMatch(mode, m);
        toast(`진행 중인 ${mode === "friendly" ? "친선전" : "랭킹전"}이 있어 이어서 들어갑니다.`, "ok");
        go("battle");
      }
    }).catch(() => {});
    return subscribe((patch) => {
      if ("online" in patch) countFriends();
      if ("profile" in patch || "mails" in patch || "deck" in patch) draw();
    });
  },
};
