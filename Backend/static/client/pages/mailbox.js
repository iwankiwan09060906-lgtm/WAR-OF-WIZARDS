// 7. 우편함 · 공지 — 왼쪽 목록(탭) + 오른쪽 읽기 (보상 수령, 초대 룸 바로 입장)
import { state, setState, subscribe, go, refreshMails, markMailRead, toast, icon, esc, headerHTML, timeAgo } from "../core.js";

const KIND = { mail: "우편", invite: "초대", notice: "공지" };

export default {
  title: "우편함 · 공지",
  render(root) {
    let tab = "mail";
    let openId = null;

    const draw = () => {
      const all = state.mails;
      const items = all.filter((m) => (tab === "notice" ? m.kind === "notice" : m.kind !== "notice"));
      const unreadOf = (t) => all.filter((m) => !m.read && (t === "notice" ? m.kind === "notice" : m.kind !== "notice")).length;
      const open = items.find((m) => m.id === openId) || null;
      root.innerHTML = `${headerHTML("우편함 · 공지")}
        <div class="body flush"><div class="mailbox">
          <section class="mail-list" aria-label="우편 목록">
            <div style="padding:14px 20px 0"><div class="tabs" role="tablist">
              <button role="tab" data-tab="mail" aria-selected="${tab === "mail"}">우편함${unreadOf("mail") ? `<span class="badge">${unreadOf("mail")}</span>` : ""}</button>
              <button role="tab" data-tab="notice" aria-selected="${tab === "notice"}">공지사항${unreadOf("notice") ? `<span class="badge">${unreadOf("notice")}</span>` : ""}</button>
            </div></div>
            ${items.length ? `<div class="list">${items.map((m) => `
              <button class="row mail-item ${m.read ? "read" : ""}" data-open="${m.id}" aria-current="${m.id === openId}">
                <span class="dot ${m.read ? "" : "on"}" aria-label="${m.read ? "읽음" : "읽지 않음"}"></span><span class="t">${esc(m.title)}</span>
                <span class="chip" style="min-height:22px;font-size:.72rem">${KIND[m.kind] || "우편"}</span>
                <span class="s">${m.from ? `${esc(m.from)} · ` : ""}${timeAgo(m.createdAt)}${m.reward ? ` · 보상 ${esc(m.reward)}` : ""}</span>
              </button>`).join("")}</div>` : `<p class="empty">${tab === "notice" ? "등록된 공지가 없습니다." : "받은 우편이 없습니다."}</p>`}
          </section>
          <section class="mail-read letter" aria-live="polite">${open ? `
            <div style="display:flex;gap:8px;align-items:center;margin-bottom:10px"><span class="chip cyan">${KIND[open.kind] || "우편"}</span><span class="hint">${open.from ? `보낸 사람: ${esc(open.from)} · ` : ""}${timeAgo(open.createdAt)}</span></div>
            <h2>${esc(open.title)}</h2><p class="letter-body">${esc(open.body)}</p>
            ${open.reward ? `<div class="attach">${icon("gift", "lg")}<div style="flex:1"><div class="label">첨부 보상</div><div class="what">${esc(open.reward)}</div></div>
              ${open.read ? `<span class="chip">${icon("check")}수령 완료</span>` : `<button class="btn play" data-claim="${open.id}">수령</button>`}</div>` : ""}
            ${open.roomCode ? `<div class="attach">${icon("door", "lg")}<div style="flex:1"><div class="label">룸 코드</div><div class="what tech" style="letter-spacing:.2em">${esc(open.roomCode)}</div></div>
              <button class="btn play" data-join="${open.id}">바로 입장</button></div>` : ""}`
            : `<div class="empty" style="margin:10vh auto 0;max-width:420px">${icon("mail", "lg")}
                <p style="margin:8px 0 18px">왼쪽에서 읽을 항목을 고르세요.</p>
                <div class="trio">
                  <div><b>${all.filter((m) => !m.read && m.kind !== "notice").length}</b><span>읽지 않은 우편</span></div>
                  <div><b>${all.filter((m) => !m.read && m.reward).length}</b><span>받을 보상</span></div>
                  <div><b>${all.filter((m) => !m.read && m.roomCode).length}</b><span>대전 초대</span></div>
                </div></div>`}
          </section>
        </div></div>`;
    };

    root.addEventListener("click", (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      const find = (id) => state.mails.find((x) => x.id === id);
      if (b.dataset.tab) { tab = b.dataset.tab; openId = null; draw(); }
      if (b.dataset.open) { const m = find(b.dataset.open); openId = m.id; if (!m.reward) markMailRead(m); else draw(); }
      if (b.dataset.claim) { const m = find(b.dataset.claim); markMailRead(m); toast(`보상을 수령했습니다: ${m.reward}`, "ok"); }
      if (b.dataset.join) {
        const m = find(b.dataset.join);
        markMailRead(m);
        setState({ room: { code: m.roomCode, role: "guest", hostName: m.from ?? "호스트" } });
        go("room");
      }
    });
    draw();
    refreshMails().catch((e) => toast(e.message, "error"));
    return subscribe((p) => { if ("mails" in p) draw(); });
  },
};
