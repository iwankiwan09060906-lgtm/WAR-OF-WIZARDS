// 룬 연습장 — 마우스로 룬을 그려 서버 인식 결과(점수 · 순위)를 확인하고, 인식된 마법을 Unity 로 시전 신호로 보내 본다
import { api, state, toast, icon, esc, headerHTML, skHTML, runeHTML, SKILLS, SKILL } from "../core.js";

const REASON = { low_score: "모양이 달라요", ambiguous: "두 룬과 비슷해요", too_few_points: "너무 짧아요", no_movement: "너무 짧아요", no_candidates: "후보가 없어요" };

export default {
  title: "룬 연습",
  render(root) {
    let only = "deck";        // 비교할 룬: deck(내 덱 8개) · all(19개)
    let target = null;        // 따라 그릴 룬
    let last = null;
    let recent = [];

    root.innerHTML = `${headerHTML("룬 연습장")}
      <div class="body"><div class="wrap practice">
        <section class="card">
          <div class="card-head"><h2 class="h2">${icon("pen")}여기에 룬을 그리세요</h2>
            <div style="display:flex;gap:8px"><div class="tabs" role="tablist"><button role="tab" data-only="deck" aria-selected="true">내 덱 8개</button><button role="tab" data-only="all" aria-selected="false">19개 전체</button></div><button class="btn ghost sm" data-act="clear">지우기</button></div></div>
          <div class="card-pad"><div class="pad big-pad" id="pad"><span class="ghost" id="ghost" hidden></span><canvas id="cv" aria-label="룬 그리기 칸"></canvas></div>
            <p class="hint" style="text-align:center;margin-top:10px">여러 획으로 그려도 됩니다. 손을 떼고 0.7초 뒤에 인식합니다.</p></div>
          <div class="card-head" style="border-top:1px solid var(--line)"><span class="label">따라 그리기 (누르면 흐리게 보임)</span></div>
          <div class="card-pad" style="display:flex;flex-wrap:wrap;gap:8px" id="targets">${SKILLS.map((s) => `<button class="btn ghost sm" data-target="${s.id}" style="padding:0 8px">${runeHTML(s.id, 28)}<span>${s.name}</span></button>`).join("")}</div>
        </section>
        <aside class="stack">
          <section class="card" id="result" aria-live="polite"></section>
          <section class="card">
            <div class="card-head"><h2 class="h2">${icon("send")}Unity 로 보낸 신호</h2><span class="hint" id="udp"></span></div>
            <div id="recent"></div>
          </section>
        </aside>
      </div></div>`;
    const $ = (s) => root.querySelector(s);

    const drawResult = () => {
      const r = last;
      $("#result").innerHTML = !r ? `<div class="card-pad"><p class="faint">아직 그린 룬이 없습니다.</p></div>` : `
        <div class="card-pad stack" style="align-items:center;text-align:center">
          <span class="chip ${r.recognized ? "cyan" : "red"}">${r.recognized ? "인식 성공" : `실패 · ${REASON[r.reason] || r.reason}`}</span>
          ${r.spellId ? `<img class="result-circle ${r.recognized ? "cast" : ""}" src="${SKILL[r.spellId].circle}" alt="">
            <div><b style="font-size:1.4rem">${r.spellId} ${esc(r.name)}</b><div class="tech faint">점수 ${Math.round(r.score * 100)} · 2위와 차이 ${Math.round(r.gap * 100)}</div></div>` : ""}
          ${r.ranking?.length ? `<div style="width:100%;text-align:left">${r.ranking.slice(0, 3).map(([c, sc], i) => `<div style="display:flex;align-items:center;gap:10px;padding:6px 0;${i ? "border-top:1px solid var(--line)" : ""}">${skHTML(c, 30)}<span style="flex:1">${c} ${SKILL[c].name}</span><b class="num">${Math.round(sc * 100)}</b></div>`).join("")}</div>` : ""}
          ${r.recognized ? `<button class="btn play block" data-cast="${r.spellId}">${icon("send")}이 마법을 Unity 로 시전</button>` : ""}
        </div>`;
    };
    const drawRecent = () => {
      $("#recent").innerHTML = recent.length ? `<div class="list">${recent.slice(0, 6).map((m) => `<div class="row">${skHTML(m.spellId, 30)}<span class="grow"><span class="t">${m.spellId} ${esc(m.name)}</span><br><span class="s">${m.phase} · ${esc(m.caster || "-")} · ${new Date(m.t * 1000).toLocaleTimeString()}</span></span></div>`).join("")}</div>` : `<p class="empty">아직 보낸 신호가 없습니다.</p>`;
    };
    const loadRecent = () => api.spellLink().then((r) => { recent = (r.recent || []).filter((m) => m.spellId); $("#udp").textContent = Array.isArray(r.unity) ? `UDP ${r.unity.join(":")}` : ""; drawRecent(); }).catch(() => {});

    // 그리기 칸
    const canvas = $("#cv");
    const g2 = canvas.getContext("2d");
    const pad = { strokes: [], cur: null, timer: 0, tone: "" };
    const fit = () => { const r = canvas.getBoundingClientRect(); const d = window.devicePixelRatio || 1; canvas.width = r.width * d; canvas.height = r.height * d; g2.setTransform(d, 0, 0, d, 0, 0); paint(); };
    const paint = () => {
      const r = canvas.getBoundingClientRect();
      g2.clearRect(0, 0, r.width, r.height);
      g2.lineWidth = Math.max(5, r.width / 44); g2.lineCap = "round"; g2.lineJoin = "round";
      g2.strokeStyle = pad.tone === "ok" ? "#3cc8ff" : pad.tone === "fail" ? "#ff5470" : "#cdefff";
      for (const s of pad.cur ? pad.strokes.concat([pad.cur]) : pad.strokes) { g2.beginPath(); s.forEach((q, k) => (k ? g2.lineTo(q.x, q.y) : g2.moveTo(q.x, q.y))); if (s.length === 1) g2.lineTo(s[0].x + 0.5, s[0].y); g2.stroke(); }
    };
    const clear = () => { clearTimeout(pad.timer); pad.strokes = []; pad.cur = null; pad.tone = ""; paint(); };
    const pt = (e) => { const r = canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    const recognize = async () => {
      const cands = only === "deck" && state.deck.length ? state.deck : null;
      try {
        last = await api.recognizeRune(pad.strokes, cands);
        pad.tone = last.recognized ? "ok" : "fail";
      } catch (e) { toast(e.message, "error"); pad.tone = "fail"; }
      paint();
      drawResult();
    };
    canvas.addEventListener("pointerdown", (e) => { e.preventDefault(); clearTimeout(pad.timer); if (pad.tone) clear(); try { canvas.setPointerCapture(e.pointerId); } catch { /* 무시 */ } pad.cur = [pt(e)]; paint(); });
    canvas.addEventListener("pointermove", (e) => { if (!pad.cur) return; const q = pt(e); const l = pad.cur[pad.cur.length - 1]; if (Math.hypot(q.x - l.x, q.y - l.y) > 2) { pad.cur.push(q); paint(); } });
    const end = () => { if (!pad.cur) return; pad.strokes.push(pad.cur); pad.cur = null; paint(); pad.timer = setTimeout(recognize, 700); };
    canvas.addEventListener("pointerup", end);
    canvas.addEventListener("pointercancel", end);

    root.addEventListener("click", async (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      if (b.dataset.only) { only = b.dataset.only; root.querySelectorAll("[data-only]").forEach((x) => x.setAttribute("aria-selected", String(x.dataset.only === only))); }
      if (b.dataset.act === "clear") clear();
      if (b.dataset.target) {
        target = target === b.dataset.target ? null : b.dataset.target;
        const gh = $("#ghost");
        gh.hidden = !target;
        if (target) gh.style.setProperty("--rune", `url('${SKILL[target].runeLine}')`);
      }
      if (b.dataset.cast) {
        await api.castSpell({ phase: "cast", spellId: b.dataset.cast, source: "web-practice" });
        toast(`${SKILL[b.dataset.cast].name} 시전 신호를 Unity 로 보냈습니다.`, "ok");
        loadRecent();
      }
    });
    const onResize = () => fit();
    window.addEventListener("resize", onResize);
    requestAnimationFrame(fit);
    drawResult();
    loadRecent();
    return () => window.removeEventListener("resize", onResize);
  },
};
