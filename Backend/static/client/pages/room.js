// 6. 친선전 룸 — 서버 룸(6자리 코드): 만들기 · 코드로 입장 · 초대 · 준비 · 시작 · 내보내기 · 나가기
//    1:1 (2자리) 은 예전 그대로, 3 · 4인 개인전 방은 서버 자리 표(members)를 그대로 그린다 (팀 없음, 빈자리는 봇)
import { api, state, setState, subscribe, go, beginMatch, beginServerMatch, updateSettings, toast, busy, icon, esc, headerHTML, deckProblem, tierOf } from "../core.js";

const CODE_RE = /^[A-Z0-9]{6}$/;

export default {
  title: "친선전 룸",
  render(root) {
    let guest = null;   // 호스트가 보는 게스트 { name, rating, bot, ready, sim } — 1:1 방
    let ready = false;  // 게스트인 내가 준비했는지
    let srv = null;     // 개인전 방: 서버가 준 방 정보 (members · maxPlayers)
    let friends = [];
    let poll = null;
    let starting = false;
    const timers = [];
    const me = () => state.profile;
    const ffa = () => (state.room?.maxPlayers || 2) > 2;
    const pick = () => ([3, 4].includes(state.settings.players) ? state.settings.players : 2);

    // side: you(나 · 청록) / foe(상대 · 분홍빨강), role: 호스트 · 게스트
    const seat = (side, role, p, sub) => p
      ? `<div class="card seat ${side}"><span class="side">${role}${side === "you" ? " · 나" : ""}</span><span class="nm">${esc(p.name)}</span><span class="hint">${p.rating ? `${tierOf(p.rating)} · ${p.rating}` : ""}</span><span class="chip ${sub.includes("준비 완료") ? "cyan" : ""}" style="align-self:flex-start;margin-top:6px">${sub}</span></div>`
      : `<div class="seat empty">게스트 자리<br><span class="hint">친구가 코드로 들어오거나 초대를 받으면 앉습니다</span></div>`;

    // 개인전 자리 하나 (나 = 청록, 나머지는 모두 상대 = 분홍빨강)
    const ffaSeat = (m, i, isHost) => {
      if (!m) {
        return `<div class="seat empty">자리 ${i + 1} · 빈자리<br><span class="hint">${isHost ? "" : "시작하면 봇이 앉습니다"}</span>${isHost ? `<button class="btn sm" data-act="bot" style="margin-top:8px">${icon("bot")}봇 채우기</button>` : ""}</div>`;
      }
      const mine = m.playerId === state.user.id;
      const sub = m.bot ? "Bot" : m.host ? "방장" : m.ready ? "준비 완료" : "준비 전";
      return `<div class="card seat ${mine ? "you" : "foe"}"><span class="side">자리 ${i + 1}${m.host ? " · HOST" : ""}${mine ? " · 나" : ""}</span>
        <span class="nm">${esc(m.nickname)}</span><span class="hint">${m.rating ? `${tierOf(m.rating)} · ${m.rating}` : ""}</span>
        <div style="display:flex;gap:6px;align-items:center;margin-top:6px"><span class="chip ${sub === "준비 완료" || sub === "방장" ? "cyan" : ""}">${sub}</span>
        ${isHost && !m.host ? `<span style="flex:1"></span><button class="btn ghost sm" data-kick="${m.memberId}">${m.bot ? "빼기" : "내보내기"}</button>` : ""}</div></div>`;
    };

    const setRoom = (room) => { setState({ room }); guest = null; ready = false; srv = null; startPolling(); draw(); };

    const inviteHTML = () => {
      const online = friends.filter((f) => state.online.includes(f.id));
      return `<section class="card"><div class="card-head"><h2 class="h2">접속 중인 친구 초대 <span class="count">${online.length}</span></h2></div>
        ${online.length ? `<div class="list">${online.map((f) => `<div class="row"><span class="dot on"></span><span class="grow"><span class="t">${esc(f.nickname)}</span><br><span class="s num">${f.rating}</span></span><button class="btn info sm" data-invite="${f.id}">${icon("send")}초대</button></div>`).join("")}</div>`
          : `<p class="empty">접속 중인 친구가 없습니다.</p>`}
      </section>`;
    };

    const drawFfa = (room, problem) => {
      const isHost = room.role === "host";
      const n = room.maxPlayers;
      const members = srv?.members || [];
      const mine = members.find((m) => m.playerId === state.user.id);
      const seats = Array.from({ length: n }, (_, i) => members[i] || null);
      root.innerHTML = `${headerHTML("친선전 룸")}
        <div class="body"><div class="wrap stack" style="max-width:1100px">
          <section class="card card-pad" style="display:flex;justify-content:space-between;align-items:center;gap:16px;flex-wrap:wrap">
            <div><div class="label">${n}인 개인전 룸 · 팀 없음</div><div class="room-code" style="margin-top:6px">${esc(room.code)}</div></div>
            <div style="display:flex;gap:8px">${isHost ? `<button class="btn sm" data-act="copy">${icon("copy")}복사</button>` : ""}<button class="btn ghost sm" data-act="leave">${isHost ? "룸 닫기" : "나가기"}</button></div>
          </section>
          <div class="ffa-seats" style="--n:${n}">${seats.map((m, i) => ffaSeat(m, i, isHost)).join("")}</div>
          <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center">
            <span class="hint">${members.length}/${n}명 · 나 빼고 모두 적 · 시작하면 빈자리는 봇이 채웁니다</span>
            <span style="flex:1"></span>
            ${isHost ? `<button class="btn play lg" data-act="start" ${problem ? "disabled" : ""}>${icon("play")}대전 시작</button>`
              : `<button class="btn play lg" data-act="ready" ${problem || mine?.ready ? "disabled" : ""}>${mine?.ready ? "방장이 시작하길 기다리는 중…" : "준비 완료"}</button>`}
          </div>
          ${problem ? `<div class="note">${esc(problem)} 마법서에서 덱을 편성해주세요.</div>` : ""}
          ${isHost ? inviteHTML() : ""}
        </div></div>`;
    };

    const draw = () => {
      const room = state.room;
      const problem = deckProblem(state.deck);
      if (!room) {
        const n = pick();
        root.innerHTML = `${headerHTML("친선전 룸")}
          <div class="body"><div class="wrap two" style="max-width:1000px">
            <section class="card card-pad stack"><h2 class="h2">${icon("door")}룸 만들기</h2>
              <p class="muted">방을 만들면 6자리 코드가 발급됩니다. 친구를 초대하거나 봇으로 빈 자리를 채울 수 있어요.</p>
              <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap"><span class="label">인원</span>
                <div class="lanes size" role="group" aria-label="인원">${[2, 3, 4].map((k) => `<button data-size="${k}" aria-pressed="${k === n}">${k > 2 ? `${k}인 개인전` : "1 : 1"}</button>`).join("")}</div></div>
              <button class="btn play lg block" data-act="create">${n > 2 ? `${n}인 개인전 룸 생성` : "룸 생성"}</button></section>
            <section class="card card-pad stack"><h2 class="h2">${icon("arrow")}코드로 입장</h2>
              <form data-join class="stack" style="gap:12px"><label class="sr-only" for="code">룸 코드</label>
                <input class="input code-input" id="code" maxlength="6" placeholder="ABC123" autocomplete="off" spellcheck="false">
                <button class="btn info lg block" type="submit">입장</button></form></section>
          </div></div>`;
        return;
      }
      if (ffa()) return drawFfa(room, problem);
      if (room.role === "host") {
        root.innerHTML = `${headerHTML("친선전 룸")}
          <div class="body"><div class="wrap stack" style="max-width:1000px">
            <section class="card card-pad" style="display:flex;justify-content:space-between;align-items:center;gap:16px;flex-wrap:wrap">
              <div><div class="label">룸 코드</div><div class="room-code" style="margin-top:6px">${esc(room.code)}</div></div>
              <div style="display:flex;gap:8px"><button class="btn sm" data-act="copy">${icon("copy")}복사</button><button class="btn ghost sm" data-act="leave">룸 닫기</button></div>
            </section>
            <div class="seats">${seat("you", "HOST", { name: me().nickname, rating: me().rating }, "준비 완료")}<span class="vs">VS</span>
              ${guest ? seat("foe", "GUEST", guest, guest.bot ? "Bot" : guest.ready ? "준비 완료" : "입장 완료") : seat("foe", "GUEST", null)}</div>
            <div style="display:flex;gap:10px;flex-wrap:wrap">
              <button class="btn" data-act="bot" ${guest ? "disabled" : ""}>${icon("bot")}봇으로 채우기</button>
              ${guest ? `<button class="btn ghost" data-act="kick">내보내기</button>` : ""}
              <span style="flex:1"></span>
              <button class="btn play lg" data-act="start" ${!guest || problem ? "disabled" : ""}>${icon("play")}대전 시작</button>
            </div>
            ${problem ? `<div class="note">${esc(problem)} 마법서에서 덱을 편성해주세요.</div>` : ""}
            ${inviteHTML()}
          </div></div>`;
      } else {
        root.innerHTML = `${headerHTML("친선전 룸")}
          <div class="body"><div class="wrap stack" style="max-width:1000px">
            <section class="card card-pad" style="display:flex;justify-content:space-between;align-items:center;gap:16px;flex-wrap:wrap">
              <div><div class="label">입장한 룸</div><div class="room-code" style="margin-top:6px">${esc(room.code)}</div></div>
              <button class="btn ghost sm" data-act="leave">나가기</button>
            </section>
            <div class="seats">${seat("foe", "HOST", { name: room.hostName, rating: room.hostRating }, "대기 중")}<span class="vs">VS</span>
              ${seat("you", "GUEST", { name: me().nickname, rating: me().rating }, ready ? "준비 완료" : "준비 전")}</div>
            ${problem ? `<div class="note">${esc(problem)} 마법서에서 덱을 편성해주세요.</div>` : ""}
            <div style="display:flex;justify-content:flex-end"><button class="btn play lg" data-act="ready" ${problem || ready ? "disabled" : ""}>${ready ? "호스트가 시작하길 기다리는 중…" : "준비 완료"}</button></div>
          </div></div>`;
      }
    };

    // 개인전 게임 방이 열리면 (서버가 만든 matchId) 게임 방 정보로 대전 시작
    const enterMatch = async () => {
      if (starting) return;
      starting = true;
      clearInterval(poll);
      try {
        const m = await api.currentMatch();
        if (!m) throw new Error("게임 방을 찾지 못했습니다.");
        beginServerMatch("friendly", m);
        go("countdown");
      } catch (e) { starting = false; toast(e.message, "error"); }
    };

    // 서버 룸 상태 확인 (1.5초마다)
    function startPolling() {
      clearInterval(poll);
      const room = state.room;
      if (!room?.server) return;
      const tick = () => api.getRoom(room.code).then((r) => {
        if (state.room?.code !== room.code) return;
        if (r.status === "CLOSED") { if (room.role === "guest") { toast("호스트가 룸을 닫았습니다.", "error"); setRoom(null); } return; }
        if ((r.maxPlayers || 2) > 2) {
          if (room.maxPlayers !== r.maxPlayers) setState({ room: { ...room, maxPlayers: r.maxPlayers } });
          if (room.role === "guest" && !r.members.some((m) => m.playerId === state.user.id)) { toast("룸에서 내보내졌습니다.", "error"); setRoom(null); return; }
          if (r.status === "PLAYING" && r.matchId) { enterMatch(); return; }
          const before = JSON.stringify(srv?.members);
          if (srv && r.members.length > srv.members.length && room.role === "host") toast(`${r.members[r.members.length - 1].nickname} 님이 입장했습니다.`, "ok");
          srv = r;
          if (JSON.stringify(r.members) !== before) draw();
          return;
        }
        if (room.role === "host") {
          if (guest && (guest.bot || guest.sim)) return;
          const next = r.guestNickname ? { name: r.guestNickname, rating: r.guestRating, bot: false, ready: !!r.guest_ready } : null;
          if (JSON.stringify(next) !== JSON.stringify(guest)) { if (next && !guest) toast(`${next.name} 님이 입장했습니다.`, "ok"); guest = next; draw(); }
        } else if (r.guest_player_id !== state.user.id) {
          toast("룸에서 내보내졌습니다.", "error"); setRoom(null);
        } else if (r.status === "PLAYING") {
          clearInterval(poll);
          beginMatch("friendly", { name: r.hostNickname, rating: r.hostRating, bot: false });
          go("countdown");
        }
      }).catch((e) => { if (e.status === 404 && state.room?.code === room.code) { toast("룸을 찾을 수 없습니다.", "error"); setRoom(null); } });
      tick();
      poll = setInterval(tick, 1500);
    }

    async function join(code, btn) {
      try {
        const r = await busy(btn, async () => { await api.joinRoom(code); return api.getRoom(code); }, "입장 중…");
        setRoom({ code, role: "guest", hostName: r.hostNickname || "호스트", hostRating: r.hostRating, server: true, maxPlayers: r.maxPlayers || 2 });
      } catch (e) { toast(e.message, "error"); }
    }

    root.addEventListener("submit", (e) => {
      e.preventDefault();
      const code = root.querySelector("#code").value.trim().toUpperCase();
      if (!CODE_RE.test(code)) return toast("룸 코드는 영문 대문자·숫자 6자리입니다.", "error");
      join(code, e.target.querySelector("button"));
    });
    root.addEventListener("input", (e) => { if (e.target.id === "code") e.target.value = e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6); });
    root.addEventListener("click", async (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      const room = state.room;
      const a = b.dataset.act;
      if (b.dataset.size) { updateSettings({ players: Number(b.dataset.size) }); draw(); return; }
      if (a === "create") {
        try {
          const r = await busy(b, () => api.createRoom(pick()), "만드는 중…");
          setRoom({ code: r.code, role: "host", hostName: me().nickname, server: true, maxPlayers: r.maxPlayers || 2 });
        } catch (err) { toast(err.message, "error"); }
      }
      if (a === "copy") { try { await navigator.clipboard.writeText(room.code); toast("룸 코드를 복사했습니다.", "ok"); } catch { toast("복사하지 못했습니다. 코드를 직접 입력해 전달해주세요.", "error"); } }
      if (a === "leave") { if (room?.server) api.leaveRoom(room.code).catch(() => {}); clearInterval(poll); setRoom(null); }
      if (ffa()) {   // 개인전 방: 모든 변경은 서버 자리 표로
        try {
          if (a === "bot") { await busy(b, () => api.roomBot(room.code), "채우는 중…"); srv = await api.getRoom(room.code); draw(); }
          if (b.dataset.kick) { await api.kickMember(room.code, Number(b.dataset.kick)); srv = await api.getRoom(room.code); draw(); }
          if (a === "ready") { await busy(b, () => api.setReady(room.code, true), "준비 중…"); srv = await api.getRoom(room.code); draw(); }
          if (a === "start") { await busy(b, () => api.startRoom(room.code), "시작 중…"); enterMatch(); }
        } catch (err) { toast(err.message, "error"); }
      } else {
        if (a === "bot") { guest = { name: "Bot", rating: me().rating, bot: true }; draw(); }
        if (a === "kick") { if (room?.server && guest && !guest.bot && !guest.sim) api.kickGuest(room.code).catch(() => {}); guest = null; draw(); }
        if (a === "start" && guest) {
          if (room?.server) api.startRoom(room.code).catch(() => {});
          beginMatch("friendly", { name: guest.name, rating: guest.rating, bot: guest.bot });
          go("countdown");
        }
        if (a === "ready") { ready = true; draw(); if (room?.server) api.setReady(room.code, true).catch((err) => toast(err.message, "error")); }
      }
      if (b.dataset.invite) {
        const f = friends.find((x) => x.id === Number(b.dataset.invite));
        try {
          await busy(b, () => api.sendInvite(state.user.id, f.id, room.code), "보내는 중…");
          toast(`${f.nickname} 님에게 초대장을 보냈습니다.`, "ok");
          if (f.npc && !ffa()) timers.push(setTimeout(() => {   // 데모 NPC 친구는 잠시 뒤 들어온 것처럼 (1:1 방)
            if (!guest && state.room?.code === room.code) { guest = { name: f.nickname, rating: f.rating, bot: false, sim: true }; toast(`${f.nickname} 님이 입장했습니다. (데모 NPC)`, "ok"); draw(); }
          }, 2500));
        } catch (err) { toast(err.message, "error"); }
      }
    });

    // 우편함 초대장으로 들어온 룸 → 서버 룸에 입장
    if (state.room && state.room.role === "guest" && !state.room.server) {
      const invited = state.room;
      setState({ room: null });
      api.joinRoom(invited.code).then(() => api.getRoom(invited.code))
        .then((r) => setRoom({ code: invited.code, role: "guest", hostName: r.hostNickname || invited.hostName, hostRating: r.hostRating, server: true, maxPlayers: r.maxPlayers || 2 }))
        .catch((err) => { toast(err.message, "error"); draw(); });
    } else startPolling();
    draw();
    api.listFriends(state.user.id).then((l) => { friends = l.filter((f) => f.relation === "friend"); if (state.room) draw(); }).catch(() => {});
    const unsub = subscribe((p) => { if ("online" in p && state.room) draw(); });
    return () => { clearInterval(poll); timers.forEach(clearTimeout); unsub(); };
  },
};
