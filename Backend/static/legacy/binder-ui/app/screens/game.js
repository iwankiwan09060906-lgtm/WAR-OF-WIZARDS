// 게임 화면 — 가운데는 Unity 게임 자리(/game/ 에 WebGL 빌드가 있으면 그대로 띄움), 둘레는 웹 HUD
//   룬 그리기 칸에 그리면 → 서버가 룬 인식(쓸 수 있는 슬롯만 후보) → 카드가 마법진으로 뒤집히고
//   → 조준(열 L/C/R) → 시전 신호를 서버로 보냄 (서버 → Unity UDP 5067 + SSE, iframe 이면 postMessage 도)
//   전투 계산(체력 · 유닛 · 승패)은 Unity 몫: Unity 가 'state' · 'match-end' 를 보내면 HUD 와 결과에 반영한다.
import { state, api, go, finishMatch, toast, confirmDialog, icon, cardHTML, tierGem, esc, fmtTime } from "../core.js";
import { SKILL, KINDS, RULES, MODE_LABEL, deckProblem } from "../data.js";

const STEPS = [
  ["Drawing", "그리기", "핀치로 룬 그리기", "pinch"],
  ["Recognizing", "인식", "룬 모양 확인", "scan"],
  ["Armed", "준비", "주먹 · 시전 준비", "fist"],
  ["Aiming", "조준", "검지로 열 가리키기", "point"],
  ["Cast", "시전", "마법진 발동!", "spark"],
];
const STEP_INDEX = { Idle: -1, Drawing: 0, Recognizing: 1, RuneReady: 2, Armed: 2, Aiming: 3, Cast: 4 };
const LANES = ["L", "C", "R"];
const LANE_KO = ["왼쪽 열", "가운데 열", "오른쪽 열"];
const REASON_KO = {
  low_score: "모양이 달라요. 마법서의 룬 그림대로 다시 그려 보세요.",
  ambiguous: "두 룬과 비슷해요. 조금 더 또렷하게!",
  too_few_points: "너무 짧아요.",
  no_movement: "너무 짧아요.",
  no_candidates: "지금 쓸 수 있는 카드가 없어요.",
};
const FALLBACK_DECK = ["S03", "S02", "S07", "S08", "S11", "S13", "H01", "H02"];

export default {
  title: "게임",
  shell: false,
  render(root) {
    const opp = state.opponent;
    if (!opp) { go("lobby"); return; }
    const deck = deckProblem(state.deck) ? FALLBACK_DECK : [...state.deck];
    const me = state.profile;
    const settings = state.settings;
    const g = {
      start: Date.now(),
      cd: deck.map(() => 0),            // 남은 쿨타임 끝나는 시각(ms)
      cdMax: deck.map((id) => (SKILL[id].high ? RULES.cooldown.high : RULES.cooldown.normal)),
      uses: deck.map((id) => (SKILL[id].high ? RULES.highUsesPerGame : Infinity)),
      phase: "Idle",
      slot: -1,                         // 시전 중인 슬롯
      aimAt: 0,
      hover: null,                      // { lane, at } — 열 위에 1초 머물면 시전
      lane: 1,                          // 내 위치
      casts: 0,
      hp: { me: RULES.maxHp, foe: RULES.maxHp, max: RULES.maxHp },
      unity: false,
      timers: [],
      ended: false,
      feed: [],
    };
    const later = (fn, ms) => g.timers.push(setTimeout(fn, ms));

    root.innerHTML = `<div class="game on-vinyl">
      <header class="hud-top">
        <div class="fighter me">${tierGem(me?.rating)}<span class="who">${esc(me?.nickname)}<small class="num">${me?.rating ?? ""}</small></span>
          <div class="hp"><div class="hp-bar" aria-label="내 체력"><i id="hp-me"></i></div><span class="hp-num num" id="hp-me-n"></span></div></div>
        <div class="clock"><div class="t num" id="clock">0:00</div><div class="m">${MODE_LABEL[state.matchMode] || ""} · <span id="stage-n">1단계</span></div></div>
        <div class="fighter enemy"><span class="who"><small class="num">${opp.rating ?? ""}</small>${esc(opp.name)}</span>${tierGem(opp.rating)}
          <div class="hp"><span class="hp-num num" id="hp-foe-n"></span><div class="hp-bar" aria-label="상대 체력"><i id="hp-foe"></i></div></div></div>
      </header>
      <div class="arena">
        <aside class="panel left-col">
          <h3>시전 상태</h3>
          <ul class="ladder" id="ladder">${STEPS.map(([k, t, d, ic]) => `<li data-step="${k}">${icon(ic)}<span><b>${t}</b><span>${d}</span></span></li>`).join("")}</ul>
          <p class="gesture-help"><b>왼손</b> 좌우 흔들기 = 이동 (← →)<br><b>오른손</b> 핀치 = 룬 그리기 · 주먹 = 준비 · 검지 = 조준 · 손바닥 = 취소<br><b>웹</b> 룬 칸에 마우스로 그리기 · 카드 클릭 / 1~8</p>
          <div style="margin-top:14px" class="link-chip" id="unity-link"><span class="dot"></span><span>Unity 연결 확인 중…</span></div>
          <div class="feed" id="feed" style="margin-top:10px" aria-live="polite"></div>
          ${["localhost", "127.0.0.1"].includes(location.hostname) ? `<details style="margin-top:12px"><summary class="hint" style="color:var(--on-vinyl-2);cursor:pointer">시연용 경기 종료</summary>
            <div style="display:flex;gap:6px;margin-top:8px"><button class="btn sm" data-end="win">승리로 끝내기</button><button class="btn sm" data-end="lose">패배로 끝내기</button></div></details>` : ""}
        </aside>
        <section class="stage" id="stage" aria-label="게임 화면">
          <div class="stage-placeholder" id="placeholder">
            <div class="stage-lanes" id="lanes">${LANES.map((l, i) => `<div class="lane" data-lane="${i}"><span class="hold"></span><span class="lane-name">${l}</span></div>`).join("")}</div>
            <div class="stage-note"><span><b>Unity 게임 화면 자리</b> · WebGL 빌드를 Backend/static/game/ 에 넣으면 여기에 뜹니다</span><span id="aim-note"></span></div>
            <div class="stage-center"><div><div class="ph" id="ph">룬을 그려 마법을 시전하세요</div><div class="ph-sub" id="ph-sub">오른쪽 칸에 그리거나 아래 카드를 누르세요</div></div></div>
            <div></div>
          </div>
        </section>
        <aside class="panel runepad-wrap">
          <div style="display:flex;justify-content:space-between;align-items:center"><h3 style="margin:0">룬 그리기</h3><button class="btn btn-quiet sm" data-act="clear">지우기</button></div>
          <div class="runepad" id="pad"><span class="guide"></span><span class="ghost" id="ghost" hidden></span><canvas id="pad-canvas" aria-label="룬 그리기 칸"></canvas></div>
          <p class="pad-msg" id="pad-msg">카드 위에 마우스를 올리면 그 룬이 흐리게 보입니다. 그대로 따라 그려 보세요.</p>
        </aside>
      </div>
      <footer class="hand-row">
        <div class="lanes-ctl"><span class="lbl">내 위치</span><div class="seg" role="group" aria-label="내 위치">${LANES.map((l, i) => `<button data-pos="${i}" aria-pressed="${i === 1}">${l}</button>`).join("")}</div></div>
        <div class="hand" id="hand">${deck.map((id, i) => `<button class="slot" data-slot="${i}" aria-label="${i + 1}번 ${SKILL[id].name}">
          ${SKILL[id].high ? `<span class="uses" id="uses-${i}">${"<i></i>".repeat(RULES.highUsesPerGame)}</span>` : ""}
          ${cardHTML(id, { size: "sm" })}<span class="key num">${i + 1}</span></button>`).join("")}</div>
        <div style="display:flex;justify-content:flex-end"><button class="btn" data-act="surrender">${icon("flag", "sm")}기권</button></div>
      </footer>
      ${settings.comfortVignette ? `<div class="vignette"></div>` : ""}
      ${settings.subtitles ? `<div class="subtitle-bar" id="subtitle" hidden></div>` : ""}
    </div>`;

    const $ = (s) => root.querySelector(s);
    const slots = [...root.querySelectorAll("#hand .slot")];

    // ── 표시 ───────────────────────────────────────────────
    const log = (text) => {
      g.feed.unshift(text);
      g.feed = g.feed.slice(0, 4);
      $("#feed").innerHTML = g.feed.map((t, i) => `<span style="opacity:${1 - i * 0.2}">${t}</span>`).join("");
      const sub = $("#subtitle");
      if (sub) { sub.hidden = false; sub.innerHTML = text; clearTimeout(sub._t); sub._t = setTimeout(() => (sub.hidden = true), 2600); }
    };
    const setPhase = (p) => {
      g.phase = p;
      const idx = STEP_INDEX[p];
      root.querySelectorAll("#ladder li").forEach((li, i) => {
        li.classList.toggle("now", i === idx);
        li.classList.toggle("done", idx > i);
      });
      const aiming = p === "Aiming";
      root.querySelectorAll(".lane").forEach((l) => l.classList.toggle("aimable", aiming));
      $("#aim-note").textContent = aiming ? "열을 클릭하거나 1초 가리키기 (Q / W / E) · Esc 취소" : "";
      slots.forEach((s, i) => {
        const picked = i === g.slot && p !== "Idle";
        s.classList.toggle("picked", picked);
        s.querySelector(".wcard").classList.toggle("is-flipped", picked && ["RuneReady", "Armed", "Aiming", "Cast"].includes(p));
      });
    };
    const drawHp = () => {
      const pct = (v) => `${Math.max(0, Math.min(100, (v / g.hp.max) * 100))}%`;
      $("#hp-me").style.setProperty("--hp", pct(g.hp.me));
      $("#hp-foe").style.setProperty("--hp", pct(g.hp.foe));
      $("#hp-me-n").textContent = `${Math.ceil(g.hp.me)}/${g.hp.max}`;
      $("#hp-foe-n").textContent = `${Math.ceil(g.hp.foe)}/${g.hp.max}`;
    };
    const showMsg = (text, tone = "") => {
      const el = $("#pad-msg");
      el.className = `pad-msg ${tone}`;
      el.textContent = text;
    };
    const burst = (id, lane, charge = false) => {
      const stage = $("#placeholder");
      if (!stage) return;
      stage.querySelectorAll(".cast-burst.charge").forEach((n) => n.remove());
      const b = document.createElement("div");
      b.className = `cast-burst ${charge ? "charge" : ""}`;
      if (lane !== null && lane !== undefined && lane >= 0) b.style.left = `${(lane + 0.5) * 33.333}%`;
      b.innerHTML = `<img src="${SKILL[id].circle}" alt="">`;
      stage.appendChild(b);
      if (!charge) {
        const label = document.createElement("div");
        label.className = "cast-label";
        label.textContent = `${SKILL[id].name}${lane >= 0 ? ` → ${LANE_KO[lane]}` : ""}`;
        stage.appendChild(label);
        later(() => { b.remove(); label.remove(); }, 1600);
      }
      $("#ph").style.opacity = "0";
      $("#ph-sub").style.opacity = "0";
    };
    const clearCharge = () => $("#placeholder")?.querySelectorAll(".cast-burst.charge").forEach((n) => n.remove());

    // ── Unity 로 보내기 (서버 경유 UDP/SSE + iframe postMessage) ───
    const frame = () => $("#unity-frame");
    const toUnity = (payload) => {
      api.castSpell({ source: "web", ...payload });
      const f = frame();
      if (f?.contentWindow) f.contentWindow.postMessage({ source: "wow-web", event: "spell", data: JSON.stringify(payload) }, location.origin);
    };

    // ── 시전 흐름 ─────────────────────────────────────────
    const ready = (i) => Date.now() >= g.cd[i] && g.uses[i] > 0;
    const reset = () => { g.slot = -1; g.hover = null; clearCharge(); setPhase("Idle"); };
    const startCast = (i, drawn = false) => {
      if (g.ended || g.phase !== "Idle") return;
      const id = deck[i];
      if (Date.now() < g.cd[i]) return showMsg("쿨타임 중입니다.", "fail");
      if (g.uses[i] <= 0) return showMsg("이번 게임의 시전 횟수를 모두 사용했습니다.", "fail");
      g.slot = i;
      const t = drawn ? [["RuneReady", 0], ["Armed", 300]] : [["Drawing", 0], ["Recognizing", 650], ["RuneReady", 850], ["Armed", 1200]];
      const total = drawn ? 600 : 1500;
      t.forEach(([p, ms]) => later(() => { if (g.slot === i) setPhase(p); }, ms));
      later(() => {
        if (g.slot !== i) return;
        toUnity({ phase: "ready", spellId: id, slot: i });
        burst(id, null, true);
        log(`<b>${SKILL[id].name}</b> 룬 인식 → 마법진 준비`);
      }, drawn ? 0 : 850);
      if (!drawn) showRuneHit(id);
      later(() => {
        if (g.slot !== i) return;
        if (KINDS[SKILL[id].kind].needsAim) { g.aimAt = Date.now(); setPhase("Aiming"); }
        else finishCast(i, -1);
      }, total);
    };
    const finishCast = (i, lane) => {
      const id = deck[i];
      setPhase("Cast");
      g.cd[i] = Date.now() + g.cdMax[i] * 1000;
      if (SKILL[id].high) g.uses[i] -= 1;
      g.casts += 1;
      clearCharge();
      burst(id, lane);
      toUnity({ phase: "cast", spellId: id, slot: i, column: lane >= 0 ? lane : null });
      log(`<b>${SKILL[id].name}</b> 시전${lane >= 0 ? ` → ${LANE_KO[lane]}` : ""}`);
      later(() => { if (g.phase === "Cast") reset(); }, 550);
    };
    const cancelAim = (msg) => {
      if (g.phase !== "Aiming") return;
      toUnity({ phase: "cancel" });
      showMsg(msg, "fail");
      log(msg);
      reset();
    };

    // ── 룬 그리기 칸 ──────────────────────────────────────
    const canvas = $("#pad-canvas");
    const pad = { strokes: [], cur: null, timer: 0, busy: false, tone: "" };
    const ctx = canvas.getContext("2d");
    const fit = () => {
      const r = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.round(r.width * dpr);
      canvas.height = Math.round(r.height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      paint();
    };
    const paint = () => {
      const r = canvas.getBoundingClientRect();
      ctx.clearRect(0, 0, r.width, r.height);
      ctx.lineWidth = Math.max(4, r.width / 40);
      ctx.lineCap = "round";
      ctx.lineJoin = "round";
      ctx.strokeStyle = pad.tone === "ok" ? "#0c9a84" : pad.tone === "fail" ? "#d0402b" : "#121829";
      for (const s of pad.cur ? pad.strokes.concat([pad.cur]) : pad.strokes) {
        ctx.beginPath();
        s.forEach((q, k) => (k ? ctx.lineTo(q.x, q.y) : ctx.moveTo(q.x, q.y)));
        if (s.length === 1) ctx.lineTo(s[0].x + 0.5, s[0].y);
        ctx.stroke();
      }
    };
    const clearPad = () => {
      clearTimeout(pad.timer);
      pad.strokes = [];
      pad.cur = null;
      pad.tone = "";
      root.querySelector("#pad .hit")?.remove();
      paint();
    };
    const showRuneHit = (id) => {
      root.querySelector("#pad .hit")?.remove();
      const hit = document.createElement("span");
      hit.className = "hit";
      hit.style.setProperty("--rune", `url('${SKILL[id].runeLine}')`);
      $("#pad").appendChild(hit);
      later(() => hit.remove(), 1400);
    };
    const pt = (e) => {
      const r = canvas.getBoundingClientRect();
      return { x: e.clientX - r.left, y: e.clientY - r.top };
    };
    const recognize = async () => {
      if (!pad.strokes.length || pad.busy) return;
      const cands = deck.filter((_, i) => ready(i));
      if (!cands.length) { pad.tone = "fail"; paint(); showMsg(REASON_KO.no_candidates, "fail"); pad.timer = setTimeout(clearPad, 1100); return; }
      if (g.phase !== "Idle") { showMsg("지금 시전 중인 마법이 끝나면 그려 주세요.", "fail"); pad.timer = setTimeout(clearPad, 900); return; }
      pad.busy = true;
      try {
        const r = await api.recognizeRune(pad.strokes, cands);
        if (r?.recognized) {
          pad.tone = "ok";
          showMsg(`${r.spellId} ${r.name} 인식 (${Math.round(r.score * 100)}점)`, "ok");
          showRuneHit(r.spellId);
          startCast(deck.indexOf(r.spellId), true);
        } else {
          pad.tone = "fail";
          showMsg(REASON_KO[r?.reason] || "인식하지 못했어요.", "fail");
        }
      } catch (e) {
        pad.tone = "fail";
        showMsg(e.message, "fail");
      } finally {
        pad.busy = false;
        paint();
        pad.timer = setTimeout(clearPad, 1100);
      }
    };
    canvas.addEventListener("pointerdown", (e) => {
      if (pad.busy || g.ended) return;
      e.preventDefault();
      clearTimeout(pad.timer);
      if (pad.tone) clearPad();
      try { canvas.setPointerCapture(e.pointerId); } catch { /* 합성 이벤트 */ }
      pad.cur = [pt(e)];
      paint();
    });
    canvas.addEventListener("pointermove", (e) => {
      if (!pad.cur) return;
      const q = pt(e);
      const l = pad.cur[pad.cur.length - 1];
      if (Math.hypot(q.x - l.x, q.y - l.y) > 2) { pad.cur.push(q); paint(); }
    });
    const endStroke = () => {
      if (!pad.cur) return;
      pad.strokes.push(pad.cur);
      pad.cur = null;
      paint();
      pad.timer = setTimeout(recognize, 650);
    };
    canvas.addEventListener("pointerup", endStroke);
    canvas.addEventListener("pointercancel", endStroke);

    // 카드에 올리면 그 룬이 칸에 흐리게 보인다 (따라 그리기 안내)
    root.querySelector("#hand").addEventListener("pointerover", (e) => {
      const s = e.target.closest(".slot");
      if (!s) return;
      const ghost = $("#ghost");
      ghost.hidden = false;
      ghost.style.setProperty("--rune", `url('${SKILL[deck[Number(s.dataset.slot)]].runeLine}')`);
    });
    root.querySelector("#hand").addEventListener("pointerleave", () => { $("#ghost").hidden = true; });

    // ── 조준 · 이동 · 버튼 ─────────────────────────────────
    const setLane = (i) => {
      g.lane = Math.max(0, Math.min(2, i));
      root.querySelectorAll("[data-pos]").forEach((b) => b.setAttribute("aria-pressed", String(Number(b.dataset.pos) === g.lane)));
      root.querySelectorAll(".lane").forEach((l, k) => {
        l.querySelector(".me-pin")?.remove();
        if (k === g.lane) l.insertAdjacentHTML("beforeend", `<span class="me-pin" aria-label="내 위치">${icon("user", "sm")}</span>`);
      });
    };
    root.addEventListener("click", async (e) => {
      const b = e.target.closest("button, .lane");
      if (!b) return;
      if (b.classList.contains("lane") && g.phase === "Aiming") return finishCast(g.slot, Number(b.dataset.lane));
      if (b.dataset.slot) return startCast(Number(b.dataset.slot));
      if (b.dataset.pos) return setLane(Number(b.dataset.pos));
      if (b.dataset.act === "clear") return clearPad();
      if (b.dataset.act === "surrender") {
        if (await confirmDialog("기권할까요?", "기권하면 패배로 기록됩니다. 랭킹전이면 레이팅이 내려갑니다.", "기권하기", true)) end("lose", "기권했습니다.");
      }
      if (b.dataset.end) end(b.dataset.end, b.dataset.end === "win" ? "시연용으로 승리 처리했습니다." : "시연용으로 패배 처리했습니다.");
    });
    root.querySelectorAll(".lane").forEach((l) => {
      l.addEventListener("pointerenter", () => { if (g.phase === "Aiming") g.hover = { lane: Number(l.dataset.lane), at: Date.now() }; });
      l.addEventListener("pointerleave", () => { g.hover = null; l.style.removeProperty("--hold"); });
    });
    const onKey = (e) => {
      if (e.target.closest?.("input, textarea") || g.ended) return;
      const k = e.key.toLowerCase();
      if (g.phase === "Aiming") {
        if (k === "q" || k === "w" || k === "e") finishCast(g.slot, "qwe".indexOf(k));
        else if (k === "escape") cancelAim("조준을 취소했습니다. (쿨타임 소모 없음)");
        return;
      }
      if (/^[1-8]$/.test(k)) startCast(Number(k) - 1);
      else if (k === "arrowleft" || k === "a") setLane(g.lane - 1);
      else if (k === "arrowright" || k === "d") setLane(g.lane + 1);
    };
    window.addEventListener("keydown", onKey);

    // ── 매 프레임: 시계 · 쿨타임 · 조준 유지 ───────────────
    let raf = 0;
    const frameLoop = () => {
      const now = Date.now();
      const sec = (now - g.start) / 1000;
      $("#clock").textContent = fmtTime(sec);
      $("#stage-n").textContent = `${Math.floor(sec / 300) + 1}단계`;
      slots.forEach((s, i) => {
        const left = Math.max(0, g.cd[i] - now) / 1000;
        let cool = s.querySelector(".cool");
        if (left > 0) {
          if (!cool) { cool = document.createElement("span"); cool.className = "cool"; s.querySelector(".wcard").appendChild(cool); }
          cool.style.setProperty("--cd", `${(left / g.cdMax[i]) * 100}%`);
          cool.textContent = Math.ceil(left);
        } else if (cool) cool.remove();
        const spent = g.uses[i] <= 0;
        if (spent && !s.querySelector(".spent")) s.querySelector(".wcard").insertAdjacentHTML("beforeend", `<span class="spent">소진</span>`);
        s.classList.toggle("ready", left <= 0 && !spent);
        const pips = s.querySelectorAll(".uses i");
        pips.forEach((p, k) => p.classList.toggle("off", k >= g.uses[i]));
      });
      if (g.phase === "Aiming") {
        if (now - g.aimAt > RULES.aimTimeout * 1000) cancelAim(`${RULES.aimTimeout}초 동안 입력이 없어 조준이 자동 취소되었습니다.`);
        else if (g.hover) {
          const held = (now - g.hover.at) / (RULES.holdToAim * 1000);
          root.querySelector(`.lane[data-lane="${g.hover.lane}"]`)?.style.setProperty("--hold", Math.min(1, held));
          if (held >= 1) { const lane = g.hover.lane; g.hover = null; root.querySelectorAll(".lane").forEach((l) => l.style.removeProperty("--hold")); finishCast(g.slot, lane); }
        }
      }
      raf = requestAnimationFrame(frameLoop);
    };

    // ── Unity 쪽에서 오는 신호 (WowBridge.jslib: postMessage · CustomEvent) ──
    const onUnity = (name, raw) => {
      let d = raw;
      try { if (typeof raw === "string") d = JSON.parse(raw); } catch { d = {}; }
      if (name === "ready") { g.unity = true; drawLink(); }
      if (name === "state" && d) {
        if (d.myHp != null) g.hp.me = d.myHp;
        if (d.enemyHp != null) g.hp.foe = d.enemyHp;
        if (d.maxHp) g.hp.max = d.maxHp;
        drawHp();
      }
      if (name === "match-end" && d) end(String(d.result || "lose").toLowerCase(), d.reason || "", d);
    };
    const onMessage = (e) => {
      if (e.origin !== location.origin || e.data?.source !== "wow-unity") return;
      onUnity(e.data.event, e.data.data);
    };
    const onCustom = (e) => onUnity(e.type.replace(/^wow:/, ""), e.detail);
    window.addEventListener("message", onMessage);
    ["wow:ready", "wow:state", "wow:match-end"].forEach((n) => window.addEventListener(n, onCustom));

    let udp = "";
    const drawLink = () => {
      const el = $("#unity-link");
      el.innerHTML = g.unity
        ? `<span class="dot on"></span><span>Unity WebGL 연결됨</span>`
        : `<span class="dot"></span><span>시전 신호 → Unity ${udp ? `UDP ${esc(udp)}` : ""}${frame() ? " · WebGL 로딩 중" : ""}</span>`;
    };
    api.spellLink().then((r) => { udp = Array.isArray(r.unity) ? r.unity.join(":") : ""; drawLink(); }).catch(() => drawLink());

    // /game/index.html 이 있으면 Unity WebGL 을 가운데에 띄운다
    fetch("game/index.html", { cache: "no-store" }).then((r) => {
      if (!r.ok || g.ended) return;
      const f = document.createElement("iframe");
      f.id = "unity-frame";
      f.title = "Unity 게임";
      f.src = "game/index.html";
      f.allow = "autoplay; fullscreen; camera; microphone";
      f.addEventListener("load", () => f.contentWindow?.postMessage({ source: "wow-web", event: "match-start",
        data: JSON.stringify({ mode: state.matchMode, opponent: opp, deck, me: { nickname: me?.nickname, rating: me?.rating } }) }, location.origin));
      $("#stage").prepend(f);
      $("#placeholder").style.display = "none";
      drawLink();
    }).catch(() => {});

    // ── 경기 끝 ───────────────────────────────────────────
    const end = (result, reason, extra = {}) => {
      if (g.ended) return;
      g.ended = true;
      finishMatch({
        result: result === "win" ? "win" : "lose",
        reason,
        durationSec: Math.floor(extra.durationSec ?? (Date.now() - g.start) / 1000),
        stats: { casts: g.casts, ...(extra.stats || {}) },
      });
      go("result");
    };

    const onResize = () => fit();
    window.addEventListener("resize", onResize);
    drawHp();
    setLane(1);
    setPhase("Idle");
    requestAnimationFrame(fit);
    raf = requestAnimationFrame(frameLoop);
    log(`<b>${esc(opp.name)}</b>와(과)의 ${MODE_LABEL[state.matchMode] || "대전"} 시작`);

    return () => {
      g.ended = true;
      cancelAnimationFrame(raf);
      g.timers.forEach(clearTimeout);
      clearTimeout(pad.timer);
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("message", onMessage);
      window.removeEventListener("resize", onResize);
      ["wow:ready", "wow:state", "wow:match-end"].forEach((n) => window.removeEventListener(n, onCustom));
    };
  },
};
