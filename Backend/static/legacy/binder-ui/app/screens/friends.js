// 친구 — 요청 보내기 · 받은 요청 · 친구 목록(접속 상태, 룸 초대) · 보낸 요청
import { state, api, subscribe, go, toast, icon, tierGem, esc, busy } from "../core.js";
import { tierOf } from "../data.js";

export default {
  title: "친구",
  shell: true,
  render(root) {
    let list = null;
    let error = "";

    const load = async () => {
      try {
        list = await api.listFriends(state.user.id);
        error = "";
      } catch (e) {
        error = e.message;
      }
      draw();
    };

    const person = (f) => `<span class="grow"><span class="name">${esc(f.nickname)}</span><br>
      <span class="sub">${tierGem(f.rating, "sm")} ${tierOf(f.rating).name} · <span class="num">${f.rating}</span></span></span>`;

    const draw = () => {
      const room = state.room;
      const incoming = (list || []).filter((f) => f.relation === "incoming");
      const friends = (list || []).filter((f) => f.relation === "friend")
        .sort((a, b) => Number(state.online.includes(b.id)) - Number(state.online.includes(a.id)));
      const outgoing = (list || []).filter((f) => f.relation === "outgoing");
      root.innerHTML = `
        <header class="page-head">
          <div><h1 class="page-title">친구 목록</h1><p class="page-lede">닉네임으로 친구를 추가하고, 접속 중인 친구를 친선전 룸에 초대하세요.</p></div>
          <div class="head-actions">
            ${room ? `<span class="chip live num">룸 ${esc(room.code)}</span>` : ""}
            <button class="btn" data-act="room">${icon("door")}${room ? "친선전 룸으로" : "친선전 룸 만들기"}</button>
          </div>
        </header>
        <form class="sheet" data-form style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap">
          <div class="field" style="flex:1;min-width:220px">
            <label class="label" for="fnick">닉네임으로 친구 요청 보내기</label>
            <input class="input" id="fnick" maxlength="12" placeholder="친구 닉네임" autocomplete="off">
          </div>
          <button class="btn btn-plate" type="submit" data-busy="보내는 중…">${icon("send", "sm")}요청</button>
        </form>
        ${error ? `<div class="notice-box" style="margin-top:16px">${esc(error)}</div>` : ""}
        ${list === null ? `<p class="empty">불러오는 중…</p>` : `
          ${incoming.length ? `<section class="sheet"><h2 class="section-title">받은 요청 <span class="count">${incoming.length}</span></h2><div class="list">
            ${incoming.map((f) => `<div class="row">${person(f)}
              <button class="btn btn-live sm" data-accept="${f.id}" data-nick="${esc(f.nickname)}">${icon("check", "sm")}수락</button>
              <button class="btn btn-quiet sm" data-decline="${f.id}">거절</button></div>`).join("")}</div></section>` : ""}
          <section class="sheet"><h2 class="section-title">친구 <span class="count">${friends.length}</span></h2>
            ${friends.length ? `<div class="list">${friends.map((f) => {
              const on = state.online.includes(f.id);
              return `<div class="row"><span class="dot ${on ? "on" : ""}" aria-hidden="true"></span>${person(f)}
                <span class="hint" style="min-width:52px">${on ? "접속 중" : "오프라인"}</span>
                <button class="btn sm" data-invite="${f.id}" data-nick="${esc(f.nickname)}" ${on ? "" : "disabled"}>${icon("mail", "sm")}초대</button>
                <button class="btn btn-quiet sm" data-remove="${f.id}" data-nick="${esc(f.nickname)}">삭제</button></div>`;
            }).join("")}</div>` : `<p class="empty">아직 친구가 없습니다. 닉네임으로 친구 요청을 보내보세요.</p>`}
          </section>
          ${outgoing.length ? `<section class="sheet"><h2 class="section-title">보낸 요청 <span class="count">${outgoing.length}</span></h2><div class="list">
            ${outgoing.map((f) => `<div class="row">${person(f)}<span class="hint">수락 대기 중</span>
              <button class="btn btn-quiet sm" data-remove="${f.id}" data-nick="${esc(f.nickname)}">취소</button></div>`).join("")}</div></section>` : ""}
        `}`;
    };

    const act = async (btn, fn, okMsg) => {
      try {
        await busy(btn, fn);
        if (okMsg) toast(okMsg, "success");
        await load();
      } catch (e) {
        toast(e.message, "error");
      }
    };

    root.addEventListener("submit", async (e) => {
      e.preventDefault();
      const input = root.querySelector("#fnick");
      const nick = input.value.trim();
      if (nick.length < 2) return toast("닉네임을 2자 이상 입력해주세요.", "error");
      await act(e.target.querySelector("button"), () => api.requestFriend(state.user.id, nick), `${nick} 님에게 친구 요청을 보냈습니다.`);
    });
    root.addEventListener("click", async (e) => {
      const b = e.target.closest("button,[data-act]");
      if (!b) return;
      const d = b.dataset;
      if (d.act === "room") go("room");
      if (d.accept) act(b, () => api.respondFriend(state.user.id, Number(d.accept), true), `${d.nick} 님과 친구가 되었습니다.`);
      if (d.decline) act(b, () => api.respondFriend(state.user.id, Number(d.decline), false));
      if (d.remove) act(b, () => api.removeFriend(state.user.id, Number(d.remove)), `${d.nick} 님을 삭제했습니다.`);
      if (d.invite) {
        const room = state.room;
        if (!room || room.role !== "host") {
          toast("먼저 친선전 룸을 만들어야 초대할 수 있습니다.", "error");
          return;
        }
        act(b, () => api.sendInvite(state.user.id, Number(d.invite), room.code), `${d.nick} 님에게 초대장을 보냈습니다. (룸 ${room.code})`);
      }
    });

    draw();
    load();
    return subscribe((patch) => { if ("online" in patch && list) draw(); });
  },
};
