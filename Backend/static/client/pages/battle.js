// 12. 전투 HUD — 가운데는 Unity 게임 화면 자리(/game/ 에 WebGL 빌드가 있으면 그대로 띄움, 맵 없음),
//   둘레는 웹 HUD: 체력 · 시간 · 시전 단계 · 룬 그리기 칸 · 마법 슬롯 8칸 · 내 위치.
//   룬을 그리면 서버가 인식 → 슬롯이 준비되고 → 조준(열) → 시전 신호를 서버로 (서버 → Unity UDP 5067 · SSE, iframe 이면 postMessage)
//   승패 · 체력은 Unity 가 정한다: Unity 가 'state' · 'match-end' 를 보내거나 서버 게임 방에 결과를 보고하면 결과 화면으로.
//   AI 전략 코치: 지금 상황(+공유한 Unity 화면)을 서버 /api/ai/strategy(VLM · 규칙 AI)로 보내 최선의 수를 받는다.
//   웹캠 손 추적: 카메라 프레임을 서버 /api/mediapipe/frame(MediaPipe)으로 보내 핀치 룬 · 주먹 · 가리키기 · 손바닥 · 왼손 이동을 받는다.
import { api, state, go, finishMatch, toast, confirmDialog, icon, esc, mmss, duelHTML, SKILL, KINDS, RULES, MODE, deckProblem } from "../core.js";

const STEPS = [["Drawing", "룬 그리기", "핀치로 그리기", "pinch"], ["Recognizing", "인식", "룬 모양 확인", "scan"], ["Armed", "준비", "주먹을 쥔다", "fist"], ["Aiming", "조준", "검지로 열 지정", "point"], ["Cast", "발동", "마법진 시전", "spark"]];
const STEP = { Idle: -1, Drawing: 0, Recognizing: 1, RuneReady: 2, Armed: 2, Aiming: 3, Cast: 4 };
const LANES = ["L", "C", "R"];
const LANE_KO = ["왼쪽 열", "가운데 열", "오른쪽 열"];
const REASON = {
  low_score: "모양이 달라요. 마법서의 룬 그림대로 다시 그려 보세요.",
  ambiguous: "두 룬과 비슷해요. 조금 더 또렷하게!",
  too_few_points: "너무 짧아요.",
  no_movement: "너무 짧아요.",
  no_candidates: "지금 쓸 수 있는 슬롯이 없어요.",
  too_short: "너무 짧아요.",
};
const POSE_KO = { pinch: "핀치", fist: "주먹", point: "가리키기", open: "손바닥", other: "-", none: "안 보임" };
const THREAT_KO = { low: ["낮음", "green"], medium: ["보통", "amber"], high: ["높음", "red"] };
const FALLBACK = ["S03", "S02", "S07", "S08", "S11", "S13", "H01", "H02"];
const LOCAL = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname);

export default {
  title: "전투",
  render(root) {
    const ctx = state.matchCtx;
    if (!ctx) { go("lobby"); return; }
    const deck = deckProblem(state.deck) ? FALLBACK : [...state.deck];
    const me = state.profile;
    const opp = ctx.opponent;
    const g = {
      start: Date.now(), cd: deck.map(() => 0), uses: deck.map((id) => (SKILL[id].high ? RULES.highUses : Infinity)),
      phase: "Idle", slot: -1, aimAt: 0, lane: 1, casts: 0, hp: { me: RULES.maxHp, foe: RULES.maxHp, max: RULES.maxHp },
      unity: false, ended: false, timers: [], feed: [], autoAim: null, unityState: null,
    };
    const later = (fn, ms) => g.timers.push(setTimeout(fn, ms));
    const cdMax = (i) => SKILL[deck[i]].cooldown ?? (SKILL[deck[i]].high ? RULES.cooldown.high : RULES.cooldown.normal);

    root.innerHTML = `<div class="battle">
      <header class="bhud">
        <div class="fighter you"><span class="who cyan">YOU · ${esc(me.nickname)}</span><div class="hp"><div class="hp-bar"><i id="hp-me"></i></div><span class="v" id="hp-me-v"></span></div></div>
        <div class="clock"><div class="t" id="clock">0:00</div><div class="m">${MODE[ctx.mode] || ""}${ctx.maxPlayers > 2 ? ` · ${ctx.maxPlayers}인 개인전` : ""}${ctx.roomCode ? ` · ${esc(ctx.roomCode)}` : ""}</div></div>
        <div class="fighter foe"><span class="who red">${esc(opp.name)} · ENEMY</span><div class="hp"><div class="hp-bar"><i id="hp-foe"></i></div><span class="v" id="hp-foe-v"></span></div></div>
      </header>
      <div class="bmain">
        <aside class="card bpanel left">
          <div class="label">시전 상태</div>
          <ol class="cast-steps" id="steps" style="margin-top:8px">${STEPS.map(([k, t, d, ic]) => `<li data-k="${k}">${icon(ic)}<span><b>${t}</b><span>${d}</span></span></li>`).join("")}</ol>
          <div class="label" style="margin-top:16px">웹 조작</div>
          <dl class="keys"><div><dt>마우스</dt><dd>오른쪽 칸에 룬 그리기</dd></div><div><dt>1 – 8</dt><dd>슬롯 바로 쓰기</dd></div><div><dt>Q W E</dt><dd>조준할 열</dd></div><div><dt>Esc</dt><dd>조준 취소</dd></div><div><dt>← →</dt><dd>내 위치 이동</dd></div></dl>
          <div class="feed" id="feed" aria-live="polite"></div>
          <div class="coach">
            <div style="display:flex;justify-content:space-between;align-items:center;gap:6px"><span class="label">AI 전략 코치</span><span class="chip" id="coach-src">-</span></div>
            <div id="coach-body" class="hint" style="margin-top:6px">지금 상황${ctx.maxPlayers > 2 ? `(${ctx.maxPlayers}인 개인전)` : ""}을 보고 최선의 수를 알려 줍니다. 화면을 보여주면 Unity 화면까지 읽습니다.</div>
            <div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:8px"><button class="btn info sm" data-act="coach">${icon("spark")}AI 전략</button><button class="btn ghost sm" data-act="share" id="share-btn">${icon("eye")}화면 보여주기</button></div>
          </div>
          ${LOCAL ? `<div style="margin-top:14px;padding-top:12px;border-top:1px dashed var(--line-2)"><div class="label">Unity 없이 시험</div><p class="hint" style="margin-top:4px">WebGL 빌드를 Backend/static/game/ 에 넣으면 가운데에 Unity 게임이 뜹니다.</p>
            <div style="display:flex;gap:6px;flex-wrap:wrap;margin-top:8px">${ctx.matchId ? `<button class="btn sm" data-dev="join">Unity 접속 흉내</button>` : ""}<button class="btn sm" data-dev="win">승리로 끝내기</button><button class="btn sm" data-dev="lose">패배로 끝내기</button></div></div>` : ""}
        </aside>
        <section class="unity-stage" id="stage" aria-label="Unity 게임 화면">
          ${duelHTML()}
          <div class="stage-top"><span><b>Unity 게임 화면</b></span><span class="chip" id="unity-chip"><span class="dot wait"></span>Unity 연결 대기</span></div>
          <div class="stage-mid" id="stage-mid"><b>룬을 그려 마법을 시전하세요</b><span class="hint">오른쪽 칸에 그리거나 아래 슬롯을 누르세요</span></div>
          <div class="lane-pick" id="lane-pick" hidden>${LANES.map((l, i) => `<button data-lane="${i}">${l} · ${"QWE"[i]}</button>`).join("")}</div>
        </section>
        <aside class="card bpanel">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:10px"><span class="label">룬 그리기</span><button class="btn ghost sm" data-act="clear">지우기</button></div>
          <div class="pad" id="pad"><span class="ghost" id="ghost" hidden></span><canvas id="pad-canvas" aria-label="룬 그리기 칸"></canvas></div>
          <p class="pad-msg" id="pad-msg">슬롯에 마우스를 올리면 그 룬이 흐리게 보입니다. 따라 그려 보세요.</p>
          <div class="coach">
            <span class="label">웹캠 손 추적 · 서버 MediaPipe</span>
            <div class="cam" id="cam" hidden><video id="cam-video" autoplay muted playsinline></video></div>
            <p class="hint" id="cam-msg" style="margin-top:6px">핀치로 룬 그리기 → 검지로 열 가리키기. 손바닥 = 취소, 왼손 휘두르기 = 이동</p>
            <button class="btn sm" data-act="cam" id="cam-btn" style="margin-top:6px">${icon("pinch")}웹캠 켜기</button>
          </div>
        </aside>
      </div>
      <footer class="bfoot">
        <div><div class="label" style="margin-bottom:6px">내 위치</div><div class="lanes" role="group" aria-label="내 위치">${LANES.map((l, i) => `<button data-pos="${i}" aria-pressed="${i === 1}">${l}</button>`).join("")}</div></div>
        <div class="hand" id="hand">${deck.map((id, i) => `<button class="hslot ${SKILL[id].high ? "high" : ""}" data-slot="${i}" aria-label="${i + 1}번 ${SKILL[id].name}"><span class="k">${i + 1}. ${id}</span><img src="${SKILL[id].circle}" alt=""><span class="n">${SKILL[id].name}</span><span class="st">READY</span></button>`).join("")}</div>
        <div style="display:flex;justify-content:flex-end"><button class="btn danger" data-act="surrender">${icon("flag")}기권</button></div>
      </footer>
      ${state.settings.comfortVignette ? `<div style="position:fixed;inset:0;pointer-events:none;background:radial-gradient(ellipse 80% 80% at 50% 50%,transparent 60%,rgba(0,0,0,.45) 100%)"></div>` : ""}
      ${state.settings.subtitles ? `<div id="subtitle" hidden style="position:fixed;left:50%;bottom:140px;translate:-50% 0;padding:10px 18px;background:rgba(0,0,0,.8);font-size:1.4rem;font-weight:700"></div>` : ""}
    </div>`;
    const $ = (s) => root.querySelector(s);
    const slots = [...root.querySelectorAll(".hslot")];

    // ── 표시 ───────────────────────────────────────────────
    const log = (html) => {
      g.feed.unshift(html);
      g.feed = g.feed.slice(0, 5);
      $("#feed").innerHTML = g.feed.map((t, i) => `<span style="opacity:${1 - i * 0.16}">${t}</span>`).join("");
      const sub = $("#subtitle");
      if (sub) { sub.hidden = false; sub.innerHTML = html; clearTimeout(sub._t); sub._t = setTimeout(() => (sub.hidden = true), 2600); }
    };
    const setPhase = (p) => {
      g.phase = p;
      const idx = STEP[p];
      root.querySelectorAll("#steps li").forEach((li, i) => { li.classList.toggle("now", i === idx); li.classList.toggle("done", idx > i); });
      $("#lane-pick").hidden = p !== "Aiming";
      slots.forEach((s, i) => s.classList.toggle("picked", i === g.slot && p !== "Idle"));
    };
    const drawHp = () => {
      const pct = (v) => String(Math.max(0, Math.min(1, v / g.hp.max)));
      $("#hp-me").style.setProperty("--hp", pct(g.hp.me));
      $("#hp-foe").style.setProperty("--hp", pct(g.hp.foe));
      $("#hp-me-v").textContent = `${Math.ceil(g.hp.me)}/${g.hp.max}`;
      $("#hp-foe-v").textContent = `${Math.ceil(g.hp.foe)}/${g.hp.max}`;
    };
    const msg = (text, tone = "") => { const el = $("#pad-msg"); el.className = `pad-msg ${tone}`; el.textContent = text; };
    const fx = (id, lane) => {
      const box = document.createElement("div");
      box.className = "cast-fx";
      if (lane >= 0) box.style.left = `${(lane + 0.5) * 33.33}%`;
      box.innerHTML = `<img src="${SKILL[id].circle}" alt="">`;
      $("#stage").appendChild(box);
      later(() => box.remove(), 1500);
      $("#stage-mid").innerHTML = `<b>${SKILL[id].name}${lane >= 0 ? ` → ${LANE_KO[lane]}` : ""}</b><span class="hint">시전 신호를 Unity 로 보냈습니다</span>`;
    };
    const drawLink = (udp) => {
      $("#unity-chip").innerHTML = g.unity ? `<span class="dot on"></span>Unity 연결됨` : `<span class="dot wait"></span>Unity 연결 대기${udp ? ` · UDP ${esc(udp)}` : ""}`;
    };

    // ── Unity 로 보내기 ────────────────────────────────────
    const toUnity = (payload) => {
      api.castSpell({ source: "web", ...payload });
      const f = $("#unity-frame");
      if (f?.contentWindow) f.contentWindow.postMessage({ source: "wow-web", event: "spell", data: JSON.stringify(payload) }, location.origin);
    };

    // ── 시전 흐름 ─────────────────────────────────────────
    const ready = (i) => Date.now() >= g.cd[i] && g.uses[i] > 0;
    const reset = () => { g.slot = -1; g.autoAim = null; setPhase("Idle"); };
    const startCast = (i, drawn = false) => {
      if (g.ended || g.phase !== "Idle" || i < 0) return;
      const id = deck[i];
      if (Date.now() < g.cd[i]) return msg("쿨타임 중입니다.", "fail");
      if (g.uses[i] <= 0) return msg("이번 게임의 시전 횟수를 모두 사용했습니다.", "fail");
      g.slot = i;
      const steps = drawn ? [["RuneReady", 0], ["Armed", 300]] : [["Drawing", 0], ["Recognizing", 650], ["RuneReady", 850], ["Armed", 1200]];
      steps.forEach(([p, ms]) => later(() => { if (g.slot === i) setPhase(p); }, ms));
      later(() => { if (g.slot === i) { toUnity({ phase: "ready", spellId: id, slot: i }); log(`<b>${SKILL[id].name}</b> 룬 인식 → 마법진 준비`); } }, drawn ? 0 : 850);
      later(() => {
        if (g.slot !== i) return;
        if (KINDS[SKILL[id].kind].needsAim) {
          g.aimAt = Date.now();
          setPhase("Aiming");
          if (g.autoAim != null) { const col = g.autoAim; g.autoAim = null; later(() => { if (g.phase === "Aiming" && g.slot === i) finishCast(i, col); }, 250); }
        }
        else finishCast(i, -1);
      }, drawn ? 600 : 1500);
    };
    const finishCast = (i, lane) => {
      const id = deck[i];
      recordCast(id, i, lane);   // AI 학습 데이터 (시전 직전 상황)
      setPhase("Cast");
      g.cd[i] = Date.now() + cdMax(i) * 1000;
      if (SKILL[id].high) g.uses[i] -= 1;
      g.casts += 1;
      fx(id, lane);
      toUnity({ phase: "cast", spellId: id, slot: i, column: lane >= 0 ? lane : null });
      log(`<b>${SKILL[id].name}</b> 시전${lane >= 0 ? ` → ${LANE_KO[lane]}` : ""}`);
      later(() => { if (g.phase === "Cast") reset(); }, 550);
    };
    const cancelAim = (text) => { if (g.phase !== "Aiming") return; toUnity({ phase: "cancel" }); msg(text, "fail"); log(text); reset(); };

    // ── 룬 그리기 칸 ──────────────────────────────────────
    const canvas = $("#pad-canvas");
    const ctx2 = canvas.getContext("2d");
    const pad = { strokes: [], cur: null, timer: 0, busy: false, tone: "" };
    const fit = () => { const r = canvas.getBoundingClientRect(); const d = window.devicePixelRatio || 1; canvas.width = r.width * d; canvas.height = r.height * d; ctx2.setTransform(d, 0, 0, d, 0, 0); paint(); };
    const paint = () => {
      const r = canvas.getBoundingClientRect();
      ctx2.clearRect(0, 0, r.width, r.height);
      ctx2.lineWidth = Math.max(4, r.width / 42);
      ctx2.lineCap = "round"; ctx2.lineJoin = "round";
      ctx2.strokeStyle = pad.tone === "ok" ? "#3cc8ff" : pad.tone === "fail" ? "#ff5470" : "#cdefff";
      for (const s of pad.cur ? pad.strokes.concat([pad.cur]) : pad.strokes) {
        ctx2.beginPath();
        s.forEach((q, k) => (k ? ctx2.lineTo(q.x, q.y) : ctx2.moveTo(q.x, q.y)));
        if (s.length === 1) ctx2.lineTo(s[0].x + 0.5, s[0].y);
        ctx2.stroke();
      }
    };
    const clearPad = () => { clearTimeout(pad.timer); pad.strokes = []; pad.cur = null; pad.tone = ""; paint(); };
    const point = (e) => { const r = canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    const recognize = async () => {
      if (!pad.strokes.length || pad.busy) return;
      if (g.phase !== "Idle") { msg("지금 시전 중인 마법이 끝나면 그려 주세요.", "fail"); pad.timer = setTimeout(clearPad, 900); return; }
      const cands = deck.filter((_, i) => ready(i));
      if (!cands.length) { pad.tone = "fail"; paint(); msg(REASON.no_candidates, "fail"); pad.timer = setTimeout(clearPad, 1100); return; }
      pad.busy = true;
      try {
        const r = await api.recognizeRune(pad.strokes, cands);
        if (r?.recognized) { pad.tone = "ok"; msg(`${r.spellId} ${r.name} 인식 (${Math.round(r.score * 100)}점)`, "ok"); startCast(deck.indexOf(r.spellId), true); }
        else { pad.tone = "fail"; msg(REASON[r?.reason] || "인식하지 못했어요.", "fail"); }
      } catch (e) { pad.tone = "fail"; msg(e.message, "fail"); }
      finally { pad.busy = false; paint(); pad.timer = setTimeout(clearPad, 1100); }
    };
    canvas.addEventListener("pointerdown", (e) => {
      if (pad.busy || g.ended) return;
      e.preventDefault();
      clearTimeout(pad.timer);
      if (pad.tone) clearPad();
      try { canvas.setPointerCapture(e.pointerId); } catch { /* 무시 */ }
      pad.cur = [point(e)];
      paint();
    });
    canvas.addEventListener("pointermove", (e) => {
      if (!pad.cur) return;
      const q = point(e); const l = pad.cur[pad.cur.length - 1];
      if (Math.hypot(q.x - l.x, q.y - l.y) > 2) { pad.cur.push(q); paint(); }
    });
    const endStroke = () => { if (!pad.cur) return; pad.strokes.push(pad.cur); pad.cur = null; paint(); pad.timer = setTimeout(recognize, 650); };
    canvas.addEventListener("pointerup", endStroke);
    canvas.addEventListener("pointercancel", endStroke);
    $("#hand").addEventListener("pointerover", (e) => {
      const s = e.target.closest(".hslot");
      if (!s) return;
      const gh = $("#ghost");
      gh.hidden = false;
      gh.style.setProperty("--rune", `url('${SKILL[deck[Number(s.dataset.slot)]].runeLine}')`);
    });
    $("#hand").addEventListener("pointerleave", () => { $("#ghost").hidden = true; });

    // ── 경기 끝 ───────────────────────────────────────────
    const end = async (result, reason) => {
      if (g.ended) return;
      g.ended = true;
      await finishMatch({ result, reason, durationSec: Math.floor((Date.now() - g.start) / 1000), stats: { casts: g.casts } });
      go("result");
    };

    // ── 버튼 · 키보드 ──────────────────────────────────────
    const setLane = (i) => { g.lane = Math.max(0, Math.min(2, i)); root.querySelectorAll("[data-pos]").forEach((b) => b.setAttribute("aria-pressed", String(Number(b.dataset.pos) === g.lane))); };
    root.addEventListener("click", async (e) => {
      const b = e.target.closest("button");
      if (!b) return;
      const d = b.dataset;
      if (d.slot) startCast(Number(d.slot));
      if (d.lane && g.phase === "Aiming") finishCast(g.slot, Number(d.lane));
      if (d.pos) setLane(Number(d.pos));
      if (d.act === "clear") clearPad();
      if (d.act === "surrender" && (await confirmDialog("기권할까요?", "기권하면 패배로 기록됩니다. 랭킹전이면 레이팅이 내려갑니다.", "기권하기", true))) end("lose", "기권했습니다.");
      if (d.dev === "join") { try { await api.matchJoin(ctx.matchId); toast("Unity 접속으로 표시했습니다.", "ok"); } catch (ex) { toast(ex.message, "error"); } }
      if (d.dev === "win") end("win", "시연용으로 승리 처리했습니다.");
      if (d.dev === "lose") end("lose", "시연용으로 패배 처리했습니다.");
    });
    const onKey = (e) => {
      if (e.target.closest?.("input, textarea") || g.ended) return;
      const k = e.key.toLowerCase();
      if (g.phase === "Aiming") {
        if ("qwe".includes(k) && k.length === 1) finishCast(g.slot, "qwe".indexOf(k));
        else if (k === "escape") cancelAim("조준을 취소했습니다. (쿨타임 소모 없음)");
        return;
      }
      if (/^[1-8]$/.test(k)) startCast(Number(k) - 1);
      else if (k === "arrowleft" || k === "a") setLane(g.lane - 1);
      else if (k === "arrowright" || k === "d") setLane(g.lane + 1);
    };
    window.addEventListener("keydown", onKey);

    // ── 시계 · 슬롯 상태 · 조준 시간 ───────────────────────
    const tick = setInterval(() => {
      const now = Date.now();
      const sec = (now - g.start) / 1000;
      $("#clock").textContent = mmss(sec);
      slots.forEach((s, i) => {
        const left = Math.max(0, g.cd[i] - now) / 1000;
        const spent = g.uses[i] <= 0;
        s.classList.toggle("cool", left > 0 && !spent);
        s.classList.toggle("spent", spent);
        s.querySelector(".st").textContent = spent ? "소진" : left > 0 ? `${left.toFixed(1)}s` : SKILL[deck[i]].high ? `READY ${g.uses[i]}/3` : "READY";
      });
      if (g.phase === "Aiming" && now - g.aimAt > RULES.aimTimeout * 1000) cancelAim(`${RULES.aimTimeout}초 동안 입력이 없어 조준이 자동 취소되었습니다.`);
    }, 100);

    // ── 서버 게임 방(랭킹전): Unity 접속 · Unity 가 보고한 결과 확인 ──
    let poll = null;
    if (ctx.matchId) {
      poll = setInterval(async () => {
        try {
          const m = await api.currentMatch();
          if (!m || m.matchId !== ctx.matchId) return;
          if (m.unity?.me && !g.unity) { g.unity = true; drawLink(); toast("Unity 게임 연결됨", "ok"); }
          if (m.result && !g.ended) {   // Unity 가 결과를 이미 서버에 보고함
            g.ended = true;
            ctx.done = true;
            state.lastMatch = { result: m.result.toLowerCase(), reason: "Unity 게임에서 보고한 결과", durationSec: Math.floor((Date.now() - g.start) / 1000), stats: { casts: g.casts }, mode: ctx.mode, opponent: opp, record: { ratingDelta: m.ratingDelta } };
            go("result");
          }
          if (m.status === "CANCELLED" && !g.ended) { toast("상대가 게임 방을 나갔습니다.", "error"); end("win", "상대가 게임 방을 나갔습니다."); }
        } catch { /* 다음 주기 */ }
      }, 1500);
    }
    // Unity WebGL (같은 페이지 iframe) 신호
    const onUnity = (name, raw) => {
      let d = raw;
      try { if (typeof raw === "string") d = JSON.parse(raw); } catch { d = {}; }
      if (name === "ready") { g.unity = true; drawLink(); if (ctx.matchId) api.matchJoin(ctx.matchId).catch(() => {}); }
      if (name === "state" && d) { g.unityState = d; if (d.myHp != null) g.hp.me = d.myHp; if (d.enemyHp != null) g.hp.foe = d.enemyHp; if (d.maxHp) g.hp.max = d.maxHp; drawHp(); }
      if (name === "match-end" && d) end(String(d.result || "lose").toLowerCase() === "win" ? "win" : "lose", d.reason || "Unity 게임 결과");
    };
    const onMessage = (e) => { if (e.origin === location.origin && e.data?.source === "wow-unity") onUnity(e.data.event, e.data.data); };
    window.addEventListener("message", onMessage);
    if (state.hasUnityBuild) {
      const f = document.createElement("iframe");
      f.id = "unity-frame";
      f.title = "Unity 게임";
      f.src = `game/index.html${ctx.matchId ? `?matchId=${ctx.matchId}` : ""}`;
      f.allow = "autoplay; fullscreen; xr-spatial-tracking; camera; microphone";
      f.addEventListener("load", () => f.contentWindow?.postMessage({ source: "wow-web", event: "match-start", data: JSON.stringify({ mode: ctx.mode, matchId: ctx.matchId, opponent: opp, deck }) }, location.origin));
      $("#stage").prepend(f);
      $("#stage-mid").hidden = true;
    }
    api.spellLink().then((r) => drawLink(Array.isArray(r.unity) ? r.unity.join(":") : "")).catch(() => drawLink());

    // ── AI 전략 코치 (/api/ai/strategy) ────────────────────
    const coach = { busy: false, stream: null, video: null, last: null };
    const coachState = () => {
      const u = g.unityState || {};
      return {
        selfHp: g.hp.me, opponentHp: g.hp.foe, maxHp: g.hp.max, selfColumn: g.lane, time: (Date.now() - g.start) / 1000,
        mode: ctx.mode, players: ctx.maxPlayers || 2,
        opponents: (ctx.opponents || []).map((o) => ({ nickname: o.nickname, bot: o.bot })),
        ...u,   // Unity 가 보낸 상황 (lanes · 구조물 · 투사체 · opponents …) 이 있으면 그걸 믿는다
        deck, ready: deck.map((_, i) => ready(i)), uses: g.uses.map((x) => (x === Infinity ? 3 : x)),
      };
    };
    const grabScreen = () => {
      const v = coach.video;
      if (!v || v.readyState < 2 || !v.videoWidth) return undefined;
      const scale = Math.min(1, 1280 / v.videoWidth);
      const c = document.createElement("canvas");
      c.width = Math.round(v.videoWidth * scale);
      c.height = Math.round(v.videoHeight * scale);
      c.getContext("2d").drawImage(v, 0, 0, c.width, c.height);
      return c.toDataURL("image/jpeg", 0.8);
    };
    const drawCoach = (r) => {
      const [tk, tone] = THREAT_KO[r.threat] || ["-", ""];
      $("#coach-src").textContent = (r.source === "claude" ? "Claude VLM" : r.source === "ollama" ? "로컬 VLM" : r.source === "local" ? "학습한 VLM" : "규칙 AI") + (r.usedImage ? " · 화면" : "");
      const c = r.cast;
      const sk = c.slot >= 0 ? SKILL[deck[c.slot]] : null;
      $("#coach-body").innerHTML = `<b class="coach-sum">${esc(r.summary)}</b><span class="chip ${tone}">위험 ${tk}</span>
        ${r.move.direction ? `<div>${r.move.direction < 0 ? "← 왼쪽" : "오른쪽 →"}으로 한 칸 — ${esc(r.move.reason)}</div>` : ""}
        <div>${sk ? `추천 <b>${c.slot + 1}. ${esc(sk.name)}</b>${c.column >= 0 ? ` → ${LANE_KO[c.column]}` : ""} — ` : ""}${esc(c.reason)}</div>
        ${r.focus ? `<div class="red">노릴 상대: ${esc(r.focus)}</div>` : ""}
        ${r.plan?.length ? `<ol class="coach-plan">${r.plan.map((x) => `<li>${esc(x)}</li>`).join("")}</ol>` : ""}
        ${r.fallbackReason ? `<div>VLM 대신 규칙 AI: ${esc(r.fallbackReason)}</div>` : ""}
        ${sk ? `<button class="btn play sm" data-act="coach-go" style="margin-top:6px;align-self:flex-start">추천대로 쓰기</button>` : ""}`;
    };
    const askCoach = async () => {
      if (coach.busy || g.ended) return;
      coach.busy = true;
      $("#coach-body").innerHTML = `<span><span class="spin" aria-hidden="true"></span> 상황을 읽는 중…</span>`;
      try {
        coach.last = await api.aiStrategy({ state: coachState(), image: grabScreen(), gameId: ctx.gameId, matchId: ctx.matchId });
        drawCoach(coach.last);
        log(`AI 코치: ${esc(coach.last.summary)}`);
      } catch (e) { $("#coach-body").textContent = e.message; }
      finally { coach.busy = false; }
    };
    const followCoach = () => {
      const r = coach.last;
      if (!r) return;
      if (r.move.direction) setLane(g.lane + r.move.direction);
      const c = r.cast;
      if (c.slot >= 0 && g.phase === "Idle" && ready(c.slot)) { g.autoAim = c.column >= 0 ? c.column : null; g.followed = c.slot; startCast(c.slot); }
      else if (c.slot >= 0) msg("지금은 추천 스킬을 쓸 수 없어요 (시전 중 · 쿨타임).", "fail");
    };
    // AI 학습 데이터: 시전할 때마다 상황 + 실제 행동 (+ 공유한 화면) — 판이 끝나면 서버가 결과 이름표를 붙인다
    const recordCast = (id, i, lane) => {
      const followed = g.followed === i;
      g.followed = null;
      api.aiRecord({ gameId: ctx.gameId, matchId: ctx.matchId, source: "web", state: coachState(), image: grabScreen(),
        action: { spellId: id, slot: i, column: lane, followedAdvice: followed } }).catch(() => {});
    };
    const stopShare = () => {
      coach.stream?.getTracks().forEach((t) => t.stop());
      coach.stream = null;
      coach.video = null;
      const b = $("#share-btn");
      if (b) b.innerHTML = `${icon("eye")}화면 보여주기`;
    };
    const toggleShare = async () => {
      if (coach.stream) { stopShare(); return; }
      try {
        const st = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: 5 }, audio: false });
        const v = document.createElement("video");
        v.muted = true;
        v.srcObject = st;
        await v.play();
        coach.stream = st;
        coach.video = v;
        st.getVideoTracks()[0].addEventListener("ended", stopShare);
        $("#share-btn").innerHTML = `${icon("x")}공유 끄기`;
        toast("이제 AI 전략이 공유한 화면(Unity 게임 창)을 함께 봅니다.", "ok");
      } catch { toast("화면 공유를 하지 않았습니다.", "error"); }
    };

    // ── 웹캠 손 추적 (서버 MediaPipe /api/mediapipe/frame) ─
    const cam = { on: false, stream: null, timer: 0, busy: false, canvas: document.createElement("canvas") };
    const camMsg = (t) => { const el = $("#cam-msg"); if (el) el.textContent = t; };
    const stopCam = () => {
      const was = cam.on;
      cam.on = false;
      clearInterval(cam.timer);
      cam.stream?.getTracks().forEach((t) => t.stop());
      cam.stream = null;
      if ($("#cam")) { $("#cam").hidden = true; $("#cam-btn").innerHTML = `${icon("pinch")}웹캠 켜기`; }
      if (was) api.handReset("battle");
    };
    const onHand = (out) => {
      const r = out.right || {};
      camMsg(`오른손 ${POSE_KO[r.pose] || "-"} · 왼손 ${POSE_KO[out.left?.pose] || "-"} · 서버 ${out.ms}ms`);
      if (r.drawing && r.cursor) {          // 그리는 궤적을 룬 칸에 (커서 0~1, y 위쪽 +)
        const rect = canvas.getBoundingClientRect();
        const q = { x: r.cursor[0] * rect.width, y: (1 - r.cursor[1]) * rect.height };
        if (!pad.cur) { clearPad(); pad.cur = [q]; } else pad.cur.push(q);
        paint();
      }
      for (const ev of out.events || []) {
        if (ev.hand === "left") { if (ev.name === "move") setLane(g.lane + ev.direction); continue; }
        if (ev.name === "pinch_cancel") clearPad();
        if (ev.name === "pinch_end") {
          if (pad.cur) { pad.strokes = [pad.cur]; pad.cur = null; }
          const rn = ev.rune || {};
          if (rn.recognized && deck.includes(rn.spellId) && g.phase === "Idle") {
            pad.tone = "ok";
            msg(`${rn.spellId} ${rn.name} 인식 (손 · ${Math.round(rn.score * 100)}점)`, "ok");
            startCast(deck.indexOf(rn.spellId), true);
          } else { pad.tone = "fail"; msg(REASON[rn.reason] || "인식하지 못했어요.", "fail"); }
          paint();
          pad.timer = setTimeout(clearPad, 1100);
        }
        if (ev.name === "point" && g.phase === "Aiming") finishCast(g.slot, ev.column);
        if (ev.name === "palm_open" && g.phase === "Aiming") cancelAim("손바닥: 조준을 취소했습니다. (쿨타임 소모 없음)");
      }
    };
    const sendFrame = async () => {
      const v = $("#cam-video");
      if (!cam.on || cam.busy || g.ended || !v || v.readyState < 2) return;
      cam.busy = true;
      try {
        cam.canvas.getContext("2d").drawImage(v, 0, 0, cam.canvas.width, cam.canvas.height);
        onHand(await api.handFrame({ image: cam.canvas.toDataURL("image/jpeg", 0.7), t: performance.now(), session: "battle",
          candidates: deck.filter((_, i) => ready(i)) }));
      } catch (e) { camMsg(e.message); if (e.status === 503 || e.status === 401) stopCam(); }
      finally { cam.busy = false; }
    };
    const startCam = async () => {
      try {
        const st = await api.aiStatus();
        if (!st.mediapipe?.image?.available) {
          camMsg(`서버 MediaPipe 꺼짐 — ${st.mediapipe?.image?.reason || "Backend/run_server_ai.bat 로 서버를 켜 주세요"}`);
          return;
        }
        cam.stream = await navigator.mediaDevices.getUserMedia({ video: { width: 640, height: 480 }, audio: false });
      } catch (e) { camMsg(e.status ? e.message : "웹캠을 열 수 없습니다. 브라우저 카메라 권한을 확인하세요."); return; }
      if (g.ended) { cam.stream.getTracks().forEach((t) => t.stop()); return; }
      $("#cam-video").srcObject = cam.stream;
      $("#cam").hidden = false;
      $("#cam-btn").innerHTML = `${icon("x")}웹캠 끄기`;
      cam.canvas.width = 480;
      cam.canvas.height = 360;
      cam.on = true;
      cam.timer = setInterval(sendFrame, 80);
    };
    root.addEventListener("click", (e) => {
      const a = e.target.closest("button")?.dataset.act;
      if (a === "coach") askCoach();
      if (a === "coach-go") followCoach();
      if (a === "share") toggleShare();
      if (a === "cam") { if (cam.on) stopCam(); else startCam(); }
    });

    const onResize = () => fit();
    window.addEventListener("resize", onResize);
    drawHp();
    setPhase("Idle");
    requestAnimationFrame(fit);
    log(`<b>${esc(opp.name)}</b> 와(과)의 ${MODE[ctx.mode] || "대전"} 시작`);
    return () => {
      g.ended = true;
      g.timers.forEach(clearTimeout);
      clearInterval(tick); clearInterval(poll); clearTimeout(pad.timer);
      stopCam(); stopShare();
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("message", onMessage);
      window.removeEventListener("resize", onResize);
    };
  },
};
