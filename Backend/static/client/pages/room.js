// 6. 친선전 룸 — 서버 룸(6자리 코드): 만들기 · 코드로 입장 · 초대 · 준비 · 시작 · 내보내기 · 나가기
import { api, state, setState, subscribe, go, beginMatch, toast, busy, icon, esc, headerHTML, deckProblem, tierOf } from "../core.js";

const CODE_RE = /^[A-Z0-9]{6}$/;

export default {
  title: "친선전 룸",
  render(root) {
    let guest = null;   // 호스트가 보는 게스트 { name, rating, bot, ready, sim }
    let ready = false;  // 게스트인 내가 준비했는지
    let friends = [];
    let poll = null;
    const timers = [];
    const me = () => state.profile;

    // side: you(나 · 청록) / foe(상대 · 분홍빨강), role: 호스트 · 게스트
    const seat = (side, role, p, sub) => p
      ? `<div class="card seat ${side}"><span class="side">${role}${side === "you" ? " · 나" : ""}</span><span class="nm">${esc(p.name)}</span><span class="hint">${p.rating ? `${tierOf(p.rating)} · ${p.rating}` : ""}</span><span class="chip ${sub.includes("준비 완료") ? "cyan" : ""}" style="align-self:flex-start;margin-top:6px">${sub}</span></div>`
      : `<div class="seat empty">게스트 자리<br><span class="hint">친구가 코드로 들어오거나 초대를 받으면 앉습니다</span></div>`;

    const setRoom = (room) => { setState({ room }); guest = null; ready = false; startPolling(); draw(); };

    const draw = () => {
      const room = state.room;
      const problem = deckProblem(state.deck);
      if (!room) {
        root.innerHTML = `${headerHTML("친선전 룸")}
          <div class="body"><div class="wrap two" style="max-width:1000px">
            <section class="card card-pad stack"><h2 class="h2">${icon("door")}룸 만들기</h2>
              <p class="muted">방을 만들면 6자리 코드가 발급됩니다. 친구를 초대하거나 봇으로 빈 자리를 채울 수 있어요.</p>
              <button class="btn play lg block" data-act="create">룸 생성</button></section>
            <section class="card card-pad stack"><h2 class="h2">${icon("arrow")}코드로 입장</h2>
              <form data-join class="stack" style="gap:12px"><label class="sr-only" for="code">룸 코드</label>
                <input class="input code-input" id="code" maxlength="6" placeholder="ABC123" autocomplete="off" spellcheck="false">
                <button class="btn info lg block" type="submit">입장</button></form></section>
          </div></div>`;
        return;
      }
      const online = friends.filter((f) => state.online.includes(f.id));
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
            <section class="card"><div class="card-head"><h2 class="h2">접속 중인 친구 초대 <span class="count">${online.length}</span></h2></div>
              ${online.length ? `<div class="list">${online.map((f) => `<div class="row"><span class="dot on"></span><span class="grow"><span class="t">${esc(f.nickname)}</span><br><span class="s num">${f.rating}</span></span><button class="btn info sm" data-invite="${f.id}">${icon("send")}초대</button></div>`).join("")}</div>`
                : `<p class="empty">접속 중인 친구가 없습니다.</p>`}
            </section>
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

    // 서버 룸 상태 확인 (1.5초마다)
    function startPolling() {
      clearInterval(poll);
      const room = state.room;
      if (!room?.server) return;
      const tick = () => api.getRoom(room.code).then((r) => {
        if (state.room?.code !== room.code) return;
        if (r.status === "CLOSED") { if (room.role === "guest") { toast("호스트가 룸을 닫았습니다.", "error"); setRoom(null); } return; }
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
        setRoom({ code, role: "guest", hostName: r.hostNickname || "호스트", hostRating: r.hostRating, server: true });
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
      if (a === "create") {
        try { const r = await busy(b, () => api.createRoom(), "만드는 중…"); setRoom({ code: r.code, role: "host", hostName: me().nickname, server: true }); }
        catch (err) { toast(err.message, "error"); }
      }
      if (a === "copy") { try { await navigator.clipboard.writeText(room.code); toast("룸 코드를 복사했습니다.", "ok"); } catch { toast("복사하지 못했습니다. 코드를 직접 입력해 전달해주세요.", "error"); } }
      if (a === "leave") { if (room?.server) api.leaveRoom(room.code).catch(() => {}); clearInterval(poll); setRoom(null); }
      if (a === "bot") { guest = { name: "Bot", rating: me().rating, bot: true }; draw(); }
      if (a === "kick") { if (room?.server && guest && !guest.bot && !guest.sim) api.kickGuest(room.code).catch(() => {}); guest = null; draw(); }
      if (a === "start" && guest) {
        if (room?.server) api.startRoom(room.code).catch(() => {});
        beginMatch("friendly", { name: guest.name, rating: guest.rating, bot: guest.bot });
        go("countdown");
      }
      if (a === "ready") { ready = true; draw(); if (room?.server) api.setReady(room.code, true).catch((err) => toast(err.message, "error")); }
      if (b.dataset.invite) {
        const f = friends.find((x) => x.id === Number(b.dataset.invite));
        try {
          await busy(b, () => api.sendInvite(state.user.id, f.id, room.code), "보내는 중…");
          toast(`${f.nickname} 님에게 초대장을 보냈습니다.`, "ok");
          if (f.npc) timers.push(setTimeout(() => {   // 데모 NPC 친구는 잠시 뒤 들어온 것처럼
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
        .then((r) => setRoom({ code: invited.code, role: "guest", hostName: r.hostNickname || invited.hostName, hostRating: r.hostRating, server: true }))
        .catch((err) => { toast(err.message, "error"); draw(); });
    } else startPolling();
    draw();
    api.listFriends(state.user.id).then((l) => { friends = l.filter((f) => f.relation === "friend"); if (state.room) draw(); }).catch(() => {});
    const unsub = subscribe((p) => { if ("online" in p && state.room) draw(); });
    return () => { clearInterval(poll); timers.forEach(clearTimeout); unsub(); };
  },
};
