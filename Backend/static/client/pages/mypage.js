// 3. 마이페이지 — 프로필 · 승률 · 전적 분석 | 내 덱(HUD 순서) · 최근 대전 · 계정 관리(닉네임 · 비밀번호)
import { api, state, subscribe, refreshProfile, toast, busy, icon, esc, headerHTML, skHTML, SKILL, MODE, tierOf, winRate, mmss, timeAgo } from "../core.js";

export default {
  title: "마이페이지",
  render(root) {
    let matches = null;
    let loadErr = "";

    const ring = (pct) => {
      const c = 2 * Math.PI * 46;
      return `<div class="ring"><svg viewBox="0 0 100 100" aria-hidden="true"><circle cx="50" cy="50" r="46" fill="none" stroke="#273649" stroke-width="7"/>
        <circle cx="50" cy="50" r="46" fill="none" stroke="#3cc8ff" stroke-width="7" stroke-dasharray="${(c * pct) / 100} ${c}"/></svg>
        <div class="v">${pct}%<small>WIN RATE</small></div></div>`;
    };

    const draw = () => {
      const p = state.profile;
      const list = matches || [];
      const recent = list.slice(0, 10);
      const n = list.length;
      const avgDur = n ? list.reduce((a, m) => a + (m.durationSec || 0), 0) / n : 0;
      const avgCasts = n ? list.reduce((a, m) => a + (m.casts || 0), 0) / n : 0;
      root.innerHTML = `${headerHTML("마이페이지")}
        <div class="body"><div class="wrap mine">
          <div class="stack">
            <section class="card card-pad profile-head">
              <div class="avatar">${esc(p.nickname.slice(0, 1))}</div>
              <div class="nm">${esc(p.nickname)}</div>
              <span class="chip cyan tech">${tierOf(p.rating)} · ${p.rating}</span>
              ${ring(winRate(p.wins, p.losses))}
              <div class="trio"><div><b>${p.wins + p.losses}</b><span>전체</span></div><div><b class="cyan">${p.wins}</b><span>승</span></div><div><b class="red">${p.losses}</b><span>패</span></div></div>
            </section>
            <section class="card">
              <div class="card-head"><h2 class="h2">전적 분석 <span class="count">최근 ${n}경기</span></h2></div>
              <div class="card-pad stack" style="gap:12px">
                ${n ? `<div class="wl" aria-label="최근 경기 결과">${list.slice(0, 15).reverse().map((m) => `<i class="${m.result === "win" ? "w" : "l"}">${m.result === "win" ? "W" : "L"}</i>`).join("")}</div>
                  <div class="trio" style="grid-template-columns:1fr 1fr"><div><b>${mmss(avgDur)}</b><span>평균 플레이 시간</span></div><div><b>${avgCasts.toFixed(0)}</b><span>평균 시전 횟수</span></div></div>`
                  : `<p class="faint">기록 없음</p>`}
              </div>
            </section>
          </div>
          <div class="stack">
            <section class="card">
              <div class="card-head"><h2 class="h2">내 덱 <span class="count">HUD 슬롯 순서</span></h2><a class="btn ghost sm" href="#/spellbook">덱 편성</a></div>
              <div class="card-pad">${state.deck.length ? `<div class="deck-line">${state.deck.map((id, i) => `<figure>${skHTML(id, 52)}<span>${i + 1}. ${SKILL[id].name}</span></figure>`).join("")}</div>` : `<p class="faint">편성된 덱이 없습니다.</p>`}</div>
            </section>
            <section class="card">
              <div class="card-head"><h2 class="h2">최근 대전</h2></div>
              ${loadErr ? `<div class="card-pad"><div class="note red">${esc(loadErr)}</div></div>` : matches === null ? `<p class="empty">불러오는 중…</p>` : recent.length === 0
                ? `<p class="empty">아직 대전 기록이 없습니다. 첫 대전을 시작해보세요!</p>`
                : `<div class="table-wrap"><table class="table"><thead><tr><th>결과</th><th>모드</th><th>상대</th><th class="r hide-sm">시간</th><th class="r hide-sm">시전</th><th class="r">레이팅</th><th class="r hide-sm">언제</th></tr></thead><tbody>
                  ${recent.map((m) => `<tr>
                    <td class="tech" style="font-weight:700;color:${m.result === "win" ? "var(--cyan)" : "var(--enemy)"}">${m.result === "win" ? "VICTORY" : m.result === "draw" ? "DRAW" : "DEFEAT"}</td>
                    <td>${MODE[m.mode] || esc(m.mode)}</td><td>vs ${esc(m.opponent)}</td>
                    <td class="r num hide-sm">${mmss(m.durationSec)}</td><td class="r num hide-sm">${m.casts}회</td>
                    <td class="r num" style="color:${m.ratingDelta > 0 ? "var(--play-hi)" : m.ratingDelta < 0 ? "var(--enemy)" : "var(--text-3)"}">${m.ratingDelta > 0 ? "+" : m.ratingDelta < 0 ? "" : "±"}${m.ratingDelta || 0}</td>
                    <td class="r hide-sm faint">${timeAgo(m.endedAt)}</td></tr>`).join("")}</tbody></table></div>`}
            </section>
            <section class="card">
              <div class="card-head"><h2 class="h2">${icon("lock")}계정 관리</h2><span class="hint">${esc(state.user.email)}</span></div>
              <div class="card-pad two">
                <form data-form="nick"><div class="field"><label for="new-nick">닉네임 변경</label>
                  <div style="display:flex;gap:8px"><input class="input" id="new-nick" maxlength="12" placeholder="${esc(p.nickname)}"><button class="btn sm" type="submit">변경</button></div></div></form>
                <form data-form="pw"><div class="field"><label for="cur-pw">비밀번호 변경</label><input class="input" id="cur-pw" type="password" placeholder="현재 비밀번호" autocomplete="current-password"></div>
                  <div class="field"><input class="input" id="new-pw" type="password" placeholder="새 비밀번호 (6자 이상)" autocomplete="new-password"></div>
                  <button class="btn sm" type="submit" style="margin-top:10px">비밀번호 변경</button></form>
              </div>
            </section>
          </div>
        </div></div>`;
    };

    root.addEventListener("submit", async (e) => {
      e.preventDefault();
      const f = e.target.dataset.form;
      const btn = e.target.querySelector('[type="submit"]');
      try {
        if (f === "nick") {
          const nick = root.querySelector("#new-nick").value.trim();
          if (!/^[A-Za-z0-9가-힣_]{2,12}$/.test(nick)) return toast("닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.", "error");
          await busy(btn, () => api.changeNickname(nick), "변경 중…");
          await refreshProfile();
          toast("닉네임을 바꿨습니다.", "ok");
        }
        if (f === "pw") {
          const cur = root.querySelector("#cur-pw").value;
          const next = root.querySelector("#new-pw").value;
          if (next.length < 6) return toast("새 비밀번호는 6자 이상이어야 합니다.", "error");
          await busy(btn, () => api.changePassword(cur, next), "변경 중…");
          toast("비밀번호를 바꿨습니다.", "ok");
          e.target.reset();
        }
      } catch (ex) { toast(ex.message, "error"); }
    });

    draw();
    refreshProfile().catch(() => {});
    api.listMatches(state.user.id, 30).then((m) => { matches = m; draw(); }).catch((ex) => { loadErr = ex.message; draw(); });
    return subscribe((patch) => { if ("profile" in patch || "deck" in patch) draw(); });
  },
};
