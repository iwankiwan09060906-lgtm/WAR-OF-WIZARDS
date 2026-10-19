// 우편함 · 공지 — 왼쪽 목록(탭) + 오른쪽 읽기 창 (보상 수령, 초대 룸 바로 입장)
import { state, setState, subscribe, go, refreshMails, markMailRead, toast, icon, esc, timeAgo } from "../core.js";

const KIND = { mail: "우편", invite: "초대", notice: "공지" };

export default {
  title: "우편함",
  shell: true,
  render(root) {
    let tab = "mail";
    let openId = null;

    const draw = () => {
      const mails = state.mails;
      const items = mails.filter((m) => (tab === "notice" ? m.kind === "notice" : m.kind !== "notice"));
      const unreadOf = (t) => mails.filter((m) => !m.read && (t === "notice" ? m.kind === "notice" : m.kind !== "notice")).length;
      const open = items.find((m) => m.id === openId) || null;
      root.innerHTML = `
        <header class="page-head"><div><h1 class="page-title">우편함 · 공지</h1><p class="page-lede">보상 우편, 친선전 초대, 운영 공지가 여기에 모입니다.</p></div></header>
        <div class="mailbox">
          <section class="sheet" style="padding:14px">
            <div class="tabs" role="tablist" style="margin-bottom:8px">
              <button role="tab" data-tab="mail" aria-selected="${tab === "mail"}">우편함 ${unreadOf("mail") ? `<span class="badge">${unreadOf("mail")}</span>` : ""}</button>
              <button role="tab" data-tab="notice" aria-selected="${tab === "notice"}">공지사항 ${unreadOf("notice") ? `<span class="badge">${unreadOf("notice")}</span>` : ""}</button>
            </div>
            ${items.length ? `<div>${items.map((m) => `
              <button class="mail-item ${m.read ? "read" : ""}" data-open="${m.id}" aria-current="${m.id === openId}">
                <span class="dot ${m.read ? "" : "on"}" aria-label="${m.read ? "읽음" : "읽지 않음"}"></span>
                <span class="title">${esc(m.title)}</span>
                <span class="chip" style="min-height:22px;font-size:.75rem">${KIND[m.kind] || "우편"}</span>
                <span class="meta">${m.from ? `${esc(m.from)} · ` : ""}${timeAgo(m.createdAt)}${m.reward ? ` · 보상 ${esc(m.reward)}` : ""}</span>
              </button>`).join("")}</div>`
              : `<p class="empty">${tab === "notice" ? "등록된 공지가 없습니다." : "받은 우편이 없습니다."}</p>`}
          </section>
          <section class="sheet letter" aria-live="polite">
            ${open ? `
              <div style="display:flex;gap:8px;align-items:center;margin-bottom:10px"><span class="chip">${KIND[open.kind] || "우편"}</span><span class="hint">${open.from ? `보낸 사람: ${esc(open.from)} · ` : ""}${timeAgo(open.createdAt)}</span></div>
              <h2>${esc(open.title)}</h2>
              <p class="body">${esc(open.body)}</p>
              ${open.reward ? `<div class="attach">${icon("gift", "lg")}<span class="grow" style="flex:1"><span class="hint">첨부 보상</span><br><span class="what">${esc(open.reward)}</span></span>
                ${open.read ? `<span class="chip live">${icon("check", "sm")}수령 완료</span>` : `<button class="btn btn-plate" data-claim="${open.id}">수령</button>`}</div>` : ""}
              ${open.roomCode ? `<div class="attach">${icon("door", "lg")}<span style="flex:1"><span class="hint">룸 코드</span><br><span class="what num" style="letter-spacing:.2em">${esc(open.roomCode)}</span></span>
                <button class="btn btn-plate" data-join="${open.id}">바로 입장</button></div>` : ""}`
              : `<p class="empty">${icon("mail", "lg")}<br>왼쪽에서 항목을 선택하세요.</p>`}
          </section>
        </div>`;
    };

    root.addEventListener("click", (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      if (b.dataset.tab) { tab = b.dataset.tab; openId = null; draw(); }
      if (b.dataset.open) {
        const m = state.mails.find((x) => x.id === b.dataset.open);
        openId = m.id;
        if (!m.reward) markMailRead(m); else draw();
      }
      if (b.dataset.claim) {
        const m = state.mails.find((x) => x.id === b.dataset.claim);
        markMailRead(m);
        toast(`보상을 수령했습니다: ${m.reward}`, "success");
      }
      if (b.dataset.join) {
        const m = state.mails.find((x) => x.id === b.dataset.join);
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
