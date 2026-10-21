// 관리자 콘솔 (ROLE_ADMIN) — 현황 · 사용자 · 게임 설정 · 공지 작성 · 실시간 방송 · 로그인 기록 · 감사 로그
import { api, toast, busy, icon, esc, headerHTML } from "../core.js";

const TABS = [["dash", "현황"], ["users", "사용자"], ["configs", "게임 설정"], ["notice", "공지 · 방송"], ["logins", "로그인 기록"], ["audit", "감사 로그"]];

export default {
  title: "관리자",
  render(root) {
    let tab = "dash";
    root.innerHTML = `${headerHTML("관리자 콘솔")}
      <div class="body"><div class="wrap stack">
        <div class="tabs" role="tablist">${TABS.map(([k, l]) => `<button role="tab" data-tab="${k}" aria-selected="${k === tab}">${l}</button>`).join("")}</div>
        <div id="pane"></div>
      </div></div>`;
    const pane = root.querySelector("#pane");
    const table = (rows, cols) => rows.length
      ? `<section class="card"><div class="table-wrap"><table class="table"><thead><tr>${cols.map(([, h]) => `<th>${h}</th>`).join("")}</tr></thead><tbody>
          ${rows.map((r) => `<tr>${cols.map(([k]) => `<td>${esc(r[k] ?? "")}</td>`).join("")}</tr>`).join("")}</tbody></table></div></section>`
      : `<section class="card"><p class="empty">기록이 없습니다.</p></section>`;

    const load = async () => {
      pane.innerHTML = `<p class="empty">불러오는 중…</p>`;
      try {
        if (tab === "dash") {
          const d = await api.admin.dashboard();
          pane.innerHTML = `<div class="kpis">${[["totalUsers", "가입자"], ["totalMatches", "대전 기록"], ["gestureSamples", "제스처 샘플"], ["motionRecordings", "모션 녹화"], ["activeSseClients", "실시간 연결"]]
            .map(([k, l]) => `<div class="card kpi"><b>${d[k] ?? 0}</b><span>${l}</span></div>`).join("")}</div>
            <div class="note info" style="margin-top:16px">DB: ${esc(d.dbStatus)}</div>`;
        }
        if (tab === "users") {
          pane.innerHTML = `<form class="card card-pad" data-search style="display:flex;gap:8px"><input class="input" id="q" placeholder="이메일 · 닉네임 검색"><button class="btn" type="submit">검색</button></form><div id="users" style="margin-top:16px"></div>`;
          const show = async (q) => {
            const r = await api.admin.users(q);
            pane.querySelector("#users").innerHTML = table(r.users || [], [["id", "ID"], ["email", "이메일"], ["nickname", "닉네임"], ["rating", "레이팅"], ["role", "권한"], ["created_at", "가입일"]]);
          };
          pane.querySelector("[data-search]").addEventListener("submit", (e) => { e.preventDefault(); show(pane.querySelector("#q").value.trim()); });
          await show("");
        }
        if (tab === "configs") {
          const rows = await api.admin.configs();
          pane.innerHTML = `<section class="card"><div class="list">${rows.map((c) => `<form class="row" data-key="${esc(c.config_key)}">
              <span class="grow"><span class="t tech">${esc(c.config_key)}</span><br><span class="s">${esc(c.description || "")}</span></span>
              <input class="input" style="max-width:260px;min-height:38px" value="${esc(c.config_value)}" aria-label="${esc(c.config_key)} 값"><button class="btn sm" type="submit">저장</button></form>`).join("")}</div></section>`;
          pane.querySelectorAll("form[data-key]").forEach((f) => f.addEventListener("submit", async (e) => {
            e.preventDefault();
            try { await busy(f.querySelector("button"), () => api.admin.setConfig(f.dataset.key, f.querySelector("input").value), "저장 중…"); toast("설정을 저장했습니다. 실시간으로 반영됩니다.", "ok"); }
            catch (ex) { toast(ex.message, "error"); }
          }));
        }
        if (tab === "notice") {
          pane.innerHTML = `<div class="two">
            <form class="card card-pad" data-notice><h2 class="h2">${icon("bell")}공지 작성</h2>
              <div class="field" style="margin-top:12px"><label for="nt">제목</label><input class="input" id="nt" required></div>
              <div class="field"><label for="nb">내용</label><textarea class="input" id="nb" required></textarea></div>
              <button class="btn play" type="submit" style="margin-top:12px">공지 등록</button></form>
            <form class="card card-pad" data-cast><h2 class="h2">${icon("send")}실시간 방송</h2>
              <p class="hint" style="margin-top:6px">접속 중인 웹 · Unity 클라이언트(SSE)로 바로 보냅니다.</p>
              <div class="field" style="margin-top:12px"><label for="bm">메시지</label><input class="input" id="bm" required></div>
              <button class="btn info" type="submit" style="margin-top:12px">방송</button></form></div>`;
          pane.querySelector("[data-notice]").addEventListener("submit", async (e) => {
            e.preventDefault();
            try { await api.admin.postNotice(pane.querySelector("#nt").value.trim(), pane.querySelector("#nb").value.trim()); toast("공지를 등록했습니다.", "ok"); e.target.reset(); } catch (ex) { toast(ex.message, "error"); }
          });
          pane.querySelector("[data-cast]").addEventListener("submit", async (e) => {
            e.preventDefault();
            try { await api.admin.broadcast(pane.querySelector("#bm").value.trim()); toast("방송했습니다.", "ok"); e.target.reset(); } catch (ex) { toast(ex.message, "error"); }
          });
        }
        if (tab === "logins") pane.innerHTML = table(await api.admin.loginHistory(), [["created_at", "시간"], ["email", "이메일"], ["status", "구분"], ["failure_reason", "비고"]]);
        if (tab === "audit") pane.innerHTML = table(await api.admin.auditLogs(), [["created_at", "시간"], ["admin_user_id", "관리자"], ["action", "작업"], ["target_type", "대상"], ["details", "내용"]]);
      } catch (ex) {
        pane.innerHTML = `<div class="note red">${esc(ex.message)}</div>`;
      }
    };
    root.addEventListener("click", (e) => {
      const t = e.target.closest("[data-tab]");
      if (!t) return;
      tab = t.dataset.tab;
      root.querySelectorAll("[data-tab]").forEach((b) => b.setAttribute("aria-selected", String(b.dataset.tab === tab)));
      load();
    });
    load();
  },
};
