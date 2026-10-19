// 5. 친구 — 요청 보내기 · 받은 요청 · 친구(접속 상태, 룸 초대) · 보낸 요청
import { api, state, subscribe, toast, busy, icon, esc, headerHTML, tierOf } from "../core.js";

export default {
  title: "친구",
  render(root) {
    let list = null;
    let loadErr = "";
    const load = () => api.listFriends(state.user.id)
      .then((l) => { list = l; loadErr = ""; draw(); })
      .catch((e) => { loadErr = e.message; list = list || []; draw(); });

    const who = (f) => `<span class="grow"><span class="t">${esc(f.nickname)}</span><br><span class="s">${tierOf(f.rating)} · <span class="num">${f.rating}</span></span></span>`;

    const draw = () => {
      const room = state.room;
      const all = list || [];
      const incoming = all.filter((f) => f.relation === "incoming");
      const friends = all.filter((f) => f.relation === "friend").sort((a, b) => Number(state.online.includes(b.id)) - Number(state.online.includes(a.id)));
      const outgoing = all.filter((f) => f.relation === "outgoing");
      root.innerHTML = `${headerHTML("친구 목록", { right: `<a class="btn info sm" href="#/room">${icon("door")}${room ? `룸 ${esc(room.code)}` : "친선전 룸 만들기"}</a>` })}
        <div class="body"><div class="wrap stack" style="max-width:960px">
          <form class="card card-pad" data-form style="display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap">
            <div class="field" style="flex:1;min-width:220px"><label for="fnick">닉네임으로 친구 요청 보내기</label><input class="input" id="fnick" maxlength="12" placeholder="친구 닉네임" autocomplete="off"></div>
            <button class="btn play" type="submit">${icon("send")}요청</button>
          </form>
          ${loadErr ? `<div class="note red">${esc(loadErr)}</div>` : ""}
          ${list === null ? `<p class="empty">불러오는 중…</p>` : `
            ${incoming.length ? `<section class="card"><div class="card-head"><h2 class="h2">받은 요청 <span class="count">${incoming.length}</span></h2></div><div class="list">
              ${incoming.map((f) => `<div class="row">${who(f)}<button class="btn info sm" data-accept="${f.id}" data-nick="${esc(f.nickname)}">${icon("check")}수락</button><button class="btn ghost sm" data-decline="${f.id}">거절</button></div>`).join("")}</div></section>` : ""}
            <section class="card"><div class="card-head"><h2 class="h2">친구 <span class="count">${friends.length}</span></h2></div>
              ${friends.length ? `<div class="list">${friends.map((f) => {
                const on = state.online.includes(f.id);
                return `<div class="row"><span class="dot ${on ? "on" : ""}"></span>${who(f)}<span class="hint" style="min-width:56px">${on ? "접속 중" : "오프라인"}</span>
                  <button class="btn info sm" data-invite="${f.id}" data-nick="${esc(f.nickname)}" ${on ? "" : "disabled"}>${icon("mail")}초대</button>
                  <button class="btn ghost sm" data-remove="${f.id}" data-nick="${esc(f.nickname)}">삭제</button></div>`;
              }).join("")}</div>` : `<p class="empty">아직 친구가 없습니다. 닉네임으로 친구 요청을 보내보세요.</p>`}
            </section>
            ${outgoing.length ? `<section class="card"><div class="card-head"><h2 class="h2">보낸 요청 <span class="count">${outgoing.length}</span></h2></div><div class="list">
              ${outgoing.map((f) => `<div class="row">${who(f)}<span class="hint">수락 대기 중</span><button class="btn ghost sm" data-remove="${f.id}" data-nick="${esc(f.nickname)}">취소</button></div>`).join("")}</div></section>` : ""}`}
        </div></div>`;
    };

    const act = async (btn, fn, ok) => {
      try { await busy(btn, fn, "처리 중…"); if (ok) toast(ok, "ok"); await load(); } catch (e) { toast(e.message, "error"); }
    };
    root.addEventListener("submit", (e) => {
      e.preventDefault();
      const nick = root.querySelector("#fnick").value.trim();
      if (nick.length < 2) return toast("닉네임을 2자 이상 입력해주세요.", "error");
      act(e.target.querySelector("button"), () => api.requestFriend(state.user.id, nick), `${nick} 님에게 친구 요청을 보냈습니다.`);
    });
    root.addEventListener("click", (e) => {
      const b = e.target.closest("button[data-accept],button[data-decline],button[data-remove],button[data-invite]");
      if (!b) return;
      const d = b.dataset;
      if (d.accept) act(b, () => api.respondFriend(state.user.id, Number(d.accept), true), `${d.nick} 님과 친구가 되었습니다.`);
      if (d.decline) act(b, () => api.respondFriend(state.user.id, Number(d.decline), false));
      if (d.remove) act(b, () => api.removeFriend(state.user.id, Number(d.remove)), `${d.nick} 님을 삭제했습니다.`);
      if (d.invite) {
        const room = state.room;
        if (!room || room.role !== "host") { toast("먼저 친선전 룸을 만들어야 초대할 수 있습니다.", "error"); return; }
        act(b, () => api.sendInvite(state.user.id, Number(d.invite), room.code), `${d.nick} 님에게 초대장을 보냈습니다. (룸 ${room.code})`);
      }
    });
    draw();
    load();
    return subscribe((p) => { if ("online" in p && list) draw(); });
  },
};
