// 친선전 룸 — 서버 룸(6자리 코드): 만들기 · 코드로 입장 · 초대 · 준비 · 시작 · 내보내기 · 나가기
import { state, api, setState, subscribe, go, beginMatch, toast, icon, playerCardHTML, esc, busy } from "../core.js";
import { deckProblem } from "../data.js";

const CODE_RE = /^[A-Z0-9]{6}$/;

export default {
  title: "친선전 룸",
  shell: true,
  render(root) {
    let guest = null;     // 호스트가 보는 게스트 { name, rating, isBot, ready, sim }
    let ready = false;    // 게스트인 내가 준비했는지
    let friends = [];
    let timers = [];
    let poll = null;
    const me = () => state.profile;

    const setRoom = (room) => {
      setState({ room });
      guest = null;
      ready = false;
      startPolling();
      draw();
    };

    const seatCard = (p, label) => playerCardHTML({ nickname: p.name ?? p.nickname, rating: p.rating }, { label, compact: true });

    const draw = () => {
      const room = state.room;
      const problem = deckProblem(state.deck);
      if (!room) {
        root.innerHTML = `
          <header class="page-head"><div><h1 class="page-title">친선전 룸</h1><p class="page-lede">친구와 레이팅 걱정 없이 겨뤄 보세요. 결과는 전적에만 남습니다.</p></div></header>
          <div class="settings">
            <section class="sheet"><h2 class="section-title">룸 만들기</h2>
              <p class="muted">방을 만들면 6자리 코드가 나옵니다. 친구를 초대하거나 봇으로 빈자리를 채울 수 있어요.</p>
              <button class="btn btn-plate lg block" data-act="create" data-busy="만드는 중…" style="margin-top:18px">${icon("door")}룸 생성</button>
            </section>
            <section class="sheet"><h2 class="section-title">코드로 입장</h2>
              <form data-join style="display:flex;flex-direction:column;gap:12px">
                <label class="sr-only" for="code">룸 코드</label>
                <input class="input code-input" id="code" maxlength="6" placeholder="ABC123" autocomplete="off" spellcheck="false">
                <button class="btn lg block" type="submit" data-busy="입장 중…">입장</button>
              </form>
            </section>
          </div>`;
        return;
      }
      const online = friends.filter((f) => state.online.includes(f.id));
      if (room.role === "host") {
        root.innerHTML = `
          <header class="page-head">
            <div><h1 class="page-title">친선전 룸</h1><p class="page-lede">코드를 친구에게 알려 주거나, 접속 중인 친구를 초대하세요.</p></div>
          </header>
          <section class="sheet" style="display:flex;justify-content:space-between;align-items:center;gap:16px;flex-wrap:wrap">
            <div><div class="label">룸 코드</div><div class="room-code" aria-live="polite">${esc(room.code)}</div></div>
            <div class="head-actions"><button class="btn" data-act="copy">${icon("copy", "sm")}복사</button><button class="btn btn-quiet" data-act="leave">룸 닫기</button></div>
          </section>
          <section class="sheet">
            <div class="seats">
              ${seatCard({ name: me()?.nickname, rating: me()?.rating }, "호스트 · 준비 완료")}
              <span class="vs-mark" aria-hidden="true">VS</span>
              ${guest ? seatCard(guest, guest.isBot ? "게스트 · Bot" : guest.ready ? "게스트 · 준비 완료" : "게스트 · 입장 완료")
                : `<div class="seat-empty">게스트 자리<br><span class="hint">친구가 코드로 들어오거나 초대를 받으면 여기에 앉습니다.</span></div>`}
            </div>
            <div style="display:flex;gap:10px;flex-wrap:wrap;margin-top:18px">
              <button class="btn" data-act="bot" ${guest ? "disabled" : ""}>${icon("bot", "sm")}봇으로 채우기</button>
              ${guest ? `<button class="btn btn-quiet" data-act="kick">내보내기</button>` : ""}
              <span style="flex:1"></span>
              <button class="btn btn-plate lg" data-act="start" ${!guest || problem ? "disabled" : ""}>${icon("swords")}대전 시작</button>
            </div>
            ${problem ? `<div class="notice-box" style="margin-top:12px">${esc(problem)} 마법서에서 덱을 편성해주세요.</div>` : ""}
          </section>
          <section class="sheet"><h2 class="section-title">접속 중인 친구 초대 <span class="count">${online.length}</span></h2>
            ${online.length ? `<div class="list">${online.map((f) => `<div class="row"><span class="dot on"></span><span class="grow"><span class="name">${esc(f.nickname)}</span><br><span class="sub num">${f.rating}</span></span>
              <button class="btn sm" data-invite="${f.id}">${icon("send", "sm")}초대</button></div>`).join("")}</div>`
              : `<p class="empty">접속 중인 친구가 없습니다.</p>`}
          </section>`;
      } else {
        root.innerHTML = `
          <header class="page-head"><div><h1 class="page-title">친선전 룸</h1><p class="page-lede">준비를 누르면 호스트가 대전을 시작할 수 있습니다.</p></div></header>
          <section class="sheet" style="display:flex;justify-content:space-between;align-items:center;gap:16px;flex-wrap:wrap">
            <div><div class="label">입장한 룸</div><div class="room-code">${esc(room.code)}</div></div>
            <button class="btn btn-quiet" data-act="leave">나가기</button>
          </section>
          <section class="sheet">
            <div class="seats">
              ${seatCard({ name: room.hostName, rating: room.hostRating ?? 1000 }, "호스트 · 대기 중")}
              <span class="vs-mark" aria-hidden="true">VS</span>
              ${seatCard({ name: me()?.nickname, rating: me()?.rating }, ready ? "게스트 (나) · 준비 완료" : "게스트 (나) · 준비 전")}
            </div>
            ${problem ? `<div class="notice-box" style="margin-top:14px">${esc(problem)} 마법서에서 덱을 편성해주세요.</div>` : ""}
            <div style="display:flex;justify-content:flex-end;margin-top:18px">
              <button class="btn btn-plate lg" data-act="ready" ${problem || ready ? "disabled" : ""}>${ready ? "호스트가 시작하길 기다리는 중…" : `${icon("check")}준비 완료`}</button>
            </div>
          </section>`;
      }
    };

    // 서버 룸 상태 확인 (1.5초마다): 게스트 입장 · 준비 / 호스트 시작 · 룸 닫힘 · 내보내짐
    function startPolling() {
      clearInterval(poll);
      const room = state.room;
      if (!room?.server) return;
      const tick = () => api.getRoom(room.code).then((r) => {
        if (state.room?.code !== room.code) return;
        if (r.status === "CLOSED") {
          if (room.role === "guest") { toast("호스트가 룸을 닫았습니다.", "info"); setRoom(null); }
          return;
        }
        if (room.role === "host") {
          if (guest && (guest.isBot || guest.sim)) return;
          const next = r.guestNickname ? { name: r.guestNickname, rating: r.guestRating ?? 1000, isBot: false, ready: !!r.guest_ready } : null;
          if (JSON.stringify(next) !== JSON.stringify(guest)) {
            if (next && !guest) toast(`${next.name} 님이 입장했습니다.`, "info");
            guest = next;
            draw();
          }
        } else if (r.guest_player_id !== state.user.id) {
          toast("룸에서 내보내졌습니다.", "info");
          setRoom(null);
        } else if (r.status === "PLAYING") {
          clearInterval(poll);
          beginMatch("friendly", { name: r.hostNickname, rating: r.hostRating ?? 1000, isBot: false });
          go("countdown");
        }
      }).catch((e) => {
        if (e.status === 404 && state.room?.code === room.code) { toast("룸을 찾을 수 없습니다.", "error"); setRoom(null); }
      });
      tick();
      poll = setInterval(tick, 1500);
    }

    async function joinCode(code, btn) {
      try {
        const r = await busy(btn, async () => {
          await api.joinRoom(code);
          return api.getRoom(code);
        });
        setRoom({ code, role: "guest", hostName: r.hostNickname || "호스트", hostRating: r.hostRating, server: true });
      } catch (e) {
        toast(e.message, "error");
      }
    }

    root.addEventListener("submit", (e) => {
      e.preventDefault();
      const code = root.querySelector("#code").value.trim().toUpperCase();
      if (!CODE_RE.test(code)) return toast("룸 코드는 영문 대문자·숫자 6자리입니다.", "error");
      joinCode(code, e.target.querySelector("button"));
    });
    root.addEventListener("input", (e) => {
      if (e.target.id === "code") e.target.value = e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 6);
    });
    root.addEventListener("click", async (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      const room = state.room;
      const a = b.dataset.act;
      if (a === "create") {
        try {
          const r = await busy(b, () => api.createRoom());
          setRoom({ code: r.code, role: "host", hostName: me()?.nickname, server: true });
        } catch (err) { toast(err.message, "error"); }
      }
      if (a === "copy") {
        try { await navigator.clipboard.writeText(room.code); toast("룸 코드를 복사했습니다.", "success"); }
        catch { toast("복사하지 못했습니다. 코드를 직접 입력해 전달해주세요.", "error"); }
      }
      if (a === "leave") {
        if (room?.server) api.leaveRoom(room.code).catch(() => {});
        clearInterval(poll);
        setRoom(null);
      }
      if (a === "bot") { guest = { name: "Bot", rating: me()?.rating ?? 1000, isBot: true }; draw(); }
      if (a === "kick") {
        if (room?.server && guest && !guest.isBot && !guest.sim) api.kickGuest(room.code).catch(() => {});
        guest = null;
        draw();
      }
      if (a === "start" && guest) {
        if (room?.server) api.startRoom(room.code).catch(() => {});
        beginMatch("friendly", { name: guest.name, rating: guest.rating, isBot: guest.isBot });
        go("countdown");
      }
      if (a === "ready") {
        ready = true;
        draw();
        if (room?.server) api.setReady(room.code, true).catch((err) => toast(err.message, "error"));
      }
      if (b.dataset.invite) {
        const f = friends.find((x) => x.id === Number(b.dataset.invite));
        try {
          await busy(b, () => api.sendInvite(state.user.id, f.id, room.code));
          toast(`${f.nickname} 님에게 초대장을 보냈습니다.`, "success");
          if (f.npc) { // 데모 NPC 친구는 초대를 받으면 잠시 뒤 들어온 것처럼 보여 준다
            timers.push(setTimeout(() => {
              if (!guest && state.room?.code === room.code) {
                guest = { name: f.nickname, rating: f.rating, isBot: false, sim: true };
                toast(`${f.nickname} 님이 입장했습니다. (데모 NPC)`, "info");
                draw();
              }
            }, 2500));
          }
        } catch (err) { toast(err.message, "error"); }
      }
    });

    // 우편함 초대장으로 들어온 경우: 서버 룸에 먼저 입장
    if (state.room && state.room.role === "guest" && !state.room.server) {
      const invited = state.room;
      setState({ room: null });
      api.joinRoom(invited.code).then(() => api.getRoom(invited.code))
        .then((r) => setRoom({ code: invited.code, role: "guest", hostName: r.hostNickname || invited.hostName, hostRating: r.hostRating, server: true }))
        .catch((err) => { toast(err.message, "error"); draw(); });
    } else {
      startPolling();
    }
    draw();
    api.listFriends(state.user.id).then((l) => { friends = l.filter((f) => f.relation === "friend"); draw(); }).catch(() => {});
    const unsub = subscribe((p) => { if ("online" in p && state.room) draw(); });
    return () => { clearInterval(poll); timers.forEach(clearTimeout); unsub(); };
  },
};
