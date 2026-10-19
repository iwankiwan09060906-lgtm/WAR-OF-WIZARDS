// War of Wizards — 게임 클라이언트 화면 (웹)
// 흐름: 입장(플레이어) → 로비(게임 시작) → 파티 매칭 → 매칭 완료 · Unity 연결 → 결과
// 화면 틀: 왼쪽 UI 기둥 + 오른쪽 전장 장면. 서버 호출은 ../app/wow-server-api.js 만 쓴다.

const api = window.WOW_SERVER_API;
const app = document.getElementById("app");
const world = document.getElementById("world");
const LOCAL = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname);
const BOT_TIMEOUT = 15;

const SKILLS = {
  S01: "연막", S02: "독 장판", S03: "파이어볼", S04: "화살 세례", S05: "토네이도", S06: "번개", S07: "총난사",
  S08: "방어 쉴드", S09: "타워 버프", S10: "집중", S11: "도끼 전사", S12: "마법사", S13: "터렛",
  H01: "프리즈", H02: "레이저 빔", H03: "흡혈", H04: "반사 결계", H05: "분노", H06: "소환탑",
};

const state = { server: null, user: null, profile: null, deck: [], screen: "", match: null, hasUnityBuild: false };
let timers = [];
const every = (fn, ms) => { timers.push(setInterval(fn, ms)); };
const clearTimers = () => { timers.forEach(clearInterval); timers = []; };

// ───────────────────────── 도구 ─────────────────────────
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const mmss = (sec) => { const t = Math.max(0, Math.floor(sec)); return `${String(Math.floor(t / 60)).padStart(2, "0")}:${String(t % 60).padStart(2, "0")}`; };
const tierOf = (r = 1000) => (r >= 1500 ? "대마법사" : r >= 1300 ? "마법사" : r >= 1100 ? "숙련 마법사" : r >= 900 ? "견습 마법사" : "입문자");
const winRate = (w, l) => (w + l ? Math.round((w / (w + l)) * 100) : 0);

const ICON = {
  logout: '<path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4M10 16l-4-4 4-4M6 12h10"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5.5A1.5 1.5 0 0 0 14.5 4h-9A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16H8"/>',
  play: '<path d="M7 5v14l11-7z"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  vr: '<path d="M3.5 8.5A2.5 2.5 0 0 1 6 6h12a2.5 2.5 0 0 1 2.5 2.5v6A2.5 2.5 0 0 1 18 17h-3.2l-1.6-2.4a1.5 1.5 0 0 0-2.4 0L9.2 17H6a2.5 2.5 0 0 1-2.5-2.5z"/><circle cx="8" cy="11.5" r="1.6"/><circle cx="16" cy="11.5" r="1.6"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
};
const icon = (n) => `<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">${ICON[n]}</svg>`;

function toast(msg, tone = "") {
  let box = document.querySelector(".toasts");
  if (!box) { box = document.createElement("div"); box.className = "toasts"; box.setAttribute("aria-live", "polite"); document.body.appendChild(box); }
  const el = document.createElement("div");
  el.className = `toast ${tone}`;
  el.textContent = msg;
  box.appendChild(el);
  setTimeout(() => el.remove(), 3600);
}

async function busy(btn, fn, label) {
  const html = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = `<span class="spin" aria-hidden="true"></span>${label}`;
  try { return await fn(); } finally { if (btn.isConnected) { btn.disabled = false; btn.innerHTML = html; } }
}

// ───────────────────────── 전장 장면 ─────────────────────────
function towerSVG(roof, roofDark) {
  return `<svg viewBox="0 0 100 210" aria-hidden="true">
    <rect x="47.5" y="0" width="5" height="18" fill="#cfc6cd"/><circle cx="50" cy="4" r="4" fill="#eee6ea"/>
    <polygon points="50,14 88,66 12,66" fill="${roof}"/><polygon points="50,14 88,66 50,66" fill="${roofDark}"/>
    <rect x="10" y="64" width="80" height="7" rx="1.5" fill="#7b2f2a"/>
    <rect x="16" y="71" width="68" height="22" fill="#a4473b"/>
    ${[22, 36, 50, 64].map((x) => `<rect x="${x}" y="75" width="9" height="12" rx="1.5" fill="#2b1f22"/>`).join("")}
    <rect x="13" y="92" width="74" height="6" fill="#6e2a25"/>
    <rect x="24" y="98" width="52" height="106" fill="#8f7e6e"/><rect x="50" y="98" width="26" height="106" fill="#7b6b5d"/>
    ${[114, 142, 170].map((y) => `<rect x="35" y="${y}" width="8" height="14" rx="3" fill="#2b2124"/><rect x="57" y="${y + 6}" width="8" height="14" rx="3" fill="#2b2124"/>`).join("")}
    ${[106, 128, 152, 176, 198].map((y) => `<rect x="24" y="${y}" width="52" height="2" fill="rgba(0,0,0,.12)"/>`).join("")}
    <rect x="20" y="202" width="60" height="8" rx="2" fill="#5e5047"/>
  </svg>`;
}
const gateSVG = `<svg viewBox="0 0 120 40" aria-hidden="true"><rect x="0" y="4" width="120" height="7" fill="#81767f"/>${[4, 40, 72, 107].map((x) => `<rect x="${x}" y="11" width="9" height="29" fill="#6b6169"/>`).join("")}</svg>`;

function buildWorld() {
  // 유닛: 지평선(54%)과 파란 탑 사이 레인 위에 몇 개
  const units = [[-2.2, 60, 0], [1.6, 62, 1], [-3.5, 66, 0], [3.2, 69, 1], [-1.2, 72, 0], [2.4, 77, 1]]
    .map(([dx, y, red]) => `<span class="unit ${red ? "red" : ""}" style="left:calc(var(--vp) + ${dx}vw);top:${y}%"></span>`).join("");
  const sparks = Array.from({ length: 14 }, (_, i) => `<span class="spark" style="left:${40 + ((i * 37) % 60)}%;animation-delay:${(i * 0.8) % 10}s"></span>`).join("");
  world.innerHTML = `<div class="sky"></div><div class="ground"><span class="lane-glow"></span></div>
    <span class="obj gate-l">${gateSVG}</span><span class="obj gate-r">${gateSVG}</span>
    <span class="obj tower-foe">${towerSVG("#c7392f", "#9c2620")}</span>
    ${units}
    <span class="obj tower-mine">${towerSVG("#2f6fb8", "#22528c")}</span>
    <span class="tag foe">ENEMY</span><span class="tag you">YOU</span>
    ${sparks}`;
}
const setWorld = (mode = "") => { world.className = `world ${mode}`; };

// ───────────────────────── 공통 조각 ─────────────────────────
const emblem = `<svg viewBox="0 0 100 100" aria-hidden="true" fill="none" stroke-linecap="round" stroke-linejoin="round">
  <circle cx="50" cy="50" r="45" stroke="#ffad5a" stroke-width="5"/><path d="M24 34 36 70 50 46 64 70 76 34" stroke="#fff" stroke-width="7"/></svg>`;
const brand = () => `<div class="brand">${emblem}<span>War of Wizards · VR 1:1 마법 대전</span></div>`;

function serverChip() {
  return state.server === null ? `<span class="dot wait"></span>서버 확인 중`
    : state.server ? `<span class="dot ok"></span>파이썬 서버 연결됨` : `<span class="dot bad"></span>서버 꺼짐 — Backend/run_server.bat`;
}
function layout(column, extra = "") {
  return `<div class="screen">
    <section class="column">${column}</section>
    <section class="stage" aria-hidden="false"><div class="stage-top"><span class="chip" id="server-chip">${serverChip()}</span>${extra}</div></section>
  </div>`;
}

// 연결 단계: 1 플레이어 · 2 파이썬 서버 · 3 파티 매칭 · 4 게임 방 · 5 Unity 게임
function steps(done, now = 0) {
  const list = [["플레이어 입장", "완료"], ["파이썬 서버 연결", "완료"], ["파티 매칭", "상대 찾기"], ["게임 방 생성", "방 번호"], ["Unity 게임 접속", "대전"]];
  return `<ol class="steps" aria-label="게임 연결 단계">${list.map(([t, d], i) => {
    const n = i + 1;
    const cls = n <= done ? "done" : n === now ? "now" : "";
    const s = n <= done ? "완료" : n === now ? "진행 중" : "대기";
    return `<li class="${cls}"><span class="n">${n <= done ? icon("check") : n}</span><b>${t}</b><span class="s">${s}</span></li>`;
  }).join("")}</ol>`;
}

// ───────────────────────── 1. 입장 ─────────────────────────
function renderTitle() {
  setWorld();
  let tab = "guest";
  const form = () => (tab === "guest"
    ? `<div class="field"><label for="nick">마법사 이름</label><input class="input" id="nick" maxlength="12" placeholder="2~12자 · 한글 · 영문 · 숫자 · _" autocomplete="nickname"></div>`
    : `<div class="field"><label for="email">이메일</label><input class="input" id="email" type="email" placeholder="wizard@example.com" autocomplete="email"></div>
       <div class="field"><label for="pw">비밀번호</label><input class="input" id="pw" type="password" placeholder="6자 이상" autocomplete="current-password"></div>`);
  app.innerHTML = layout(`
    ${brand()}
    <h1 class="logo">WAR<span>OF</span>WIZARDS</h1>
    <p class="lede">손으로 룬을 그려 마법을 쏘고, 상대 탑을 무너뜨리는 VR 1:1 대전.</p>
    <div class="spacer"></div>
    <div class="form">
      <div class="tabs" role="tablist"><button role="tab" data-tab="guest" aria-selected="true">게스트로 입장</button><button role="tab" data-tab="login" aria-selected="false">계정 로그인</button></div>
      <div class="form" id="fields">${form()}</div>
      <p class="err" id="err" role="alert"></p>
      <button class="btn go sm2 block" data-act="enter">입장하기</button>
      <button class="btn link" data-act="demo" style="align-self:flex-start">데모 계정으로 바로 시작</button>
    </div>`);
  const col = app.querySelector(".column");
  const err = (m) => { col.querySelector("#err").textContent = m || ""; };
  const submit = async (b) => {
    err("");
    try {
      if (tab === "guest") {
        const nick = col.querySelector("#nick").value.trim();
        if (!/^[A-Za-z0-9가-힣_]{2,12}$/.test(nick)) return err("이름은 2~12자, 한글·영문·숫자·_ 만 쓸 수 있습니다.");
        await busy(b, () => api.guestLogin(nick), "입장 중…");
      } else {
        const email = col.querySelector("#email").value.trim();
        const pw = col.querySelector("#pw").value;
        if (!email.includes("@")) return err("올바른 이메일 주소를 입력해주세요.");
        if (pw.length < 6) return err("비밀번호는 6자 이상이어야 합니다.");
        await busy(b, () => api.signIn(email, pw), "로그인 중…");
      }
      await enter();
    } catch (ex) { err(ex.message); }
  };
  col.addEventListener("click", async (e) => {
    const t = e.target.closest("[data-tab]");
    if (t) {
      tab = t.dataset.tab;
      col.querySelectorAll("[data-tab]").forEach((x) => x.setAttribute("aria-selected", String(x.dataset.tab === tab)));
      col.querySelector("#fields").innerHTML = form();
      col.querySelector('[data-act="enter"]').textContent = tab === "guest" ? "입장하기" : "로그인";
      err("");
      col.querySelector("input")?.focus();
      return;
    }
    const b = e.target.closest("[data-act]");
    if (b?.dataset.act === "enter") submit(b);
    if (b?.dataset.act === "demo") {
      try { await busy(b, () => api.signIn("demo@wow.dev", "demo1234"), "입장 중…"); await enter(); } catch (ex) { err(ex.message); }
    }
  });
  col.addEventListener("keydown", (e) => { if (e.key === "Enter" && e.target.matches("input")) submit(col.querySelector('[data-act="enter"]')); });
  setTimeout(() => col.querySelector("#nick")?.focus(), 30);
}

// ───────────────────────── 2. 로비 ─────────────────────────
function renderLobby() {
  setWorld();
  const p = state.profile;
  const slots = state.deck.map((id, i) => `<div class="slot ${id[0] === "H" ? "high" : ""}" title="${id} ${SKILLS[id] || ""}"><span class="k">${i + 1}</span><img src="assets/skills/circle/${id}.png" alt=""><span class="n">${SKILLS[id] || id}</span></div>`).join("");
  app.innerHTML = layout(`
    ${brand()}
    <div class="me"><span class="face">${esc(p.nickname.slice(0, 1))}</span><span><span class="who">${esc(p.nickname)}</span><br><span class="meta">${tierOf(p.rating)}</span></span></div>
    <div class="stats"><div><b>${p.rating}</b><span>레이팅</span></div><div><b>${p.wins}<small style="font-size:.6em;color:var(--text-3)">승</small> ${p.losses}<small style="font-size:.6em;color:var(--text-3)">패</small></b><span>전적</span></div><div><b>${winRate(p.wins, p.losses)}%</b><span>승률</span></div></div>
    <div><p class="label" style="margin-bottom:8px">내 마법 슬롯 · 전투 HUD 순서</p>${state.deck.length ? `<div class="slots">${slots}</div>` : `<p class="lede">덱이 비어 있습니다.</p>`}</div>
    <div class="spacer"></div>
    <div>
      <button class="btn go block" data-act="start" ${state.server ? "" : "disabled"}>게임 시작</button>
      <p class="label" style="margin-top:10px;text-align:center;font-weight:500">파티 매칭 · ${BOT_TIMEOUT}초 안에 상대가 없으면 봇과 대결</p>
    </div>
    <hr class="rule">
    ${steps(2)}
  `, `<button class="btn sm" data-act="logout">${icon("logout")}나가기</button>`);
  app.addEventListener("click", onLobbyClick);
}
async function onLobbyClick(e) {
  const b = e.target.closest("[data-act]");
  if (!b || state.screen !== "lobby") return;
  if (b.dataset.act === "logout") {
    await api.signOut();
    Object.assign(state, { user: null, profile: null, deck: [], match: null });
    go("title");
  }
  if (b.dataset.act === "start") {
    try { await busy(b, () => api.joinQueue(), "대기열 등록 중…"); go("matching"); } catch (ex) { toast(ex.message, "error"); }
  }
}

// ───────────────────────── 3. 파티 매칭 ─────────────────────────
function renderMatching() {
  setWorld("searching");
  const start = Date.now();
  app.innerHTML = layout(`
    ${brand()}
    <h1 class="title-2" id="m-title">상대를 찾는 중</h1>
    <div class="timer" id="clock">00:00</div>
    <div class="bar" aria-hidden="true"><i id="m-bar"></i></div>
    <p class="lede" id="m-sub">레이팅 ${state.profile.rating} ± 50 범위에서 찾는 중</p>
    <p class="label" id="m-bot" style="font-weight:500">${BOT_TIMEOUT}초 안에 상대가 없으면 봇과 매칭됩니다</p>
    <div class="spacer"></div>
    ${steps(2, 3)}
    <button class="btn danger block" data-act="cancel">${icon("x")}매칭 취소</button>
  `);
  let polling = false;
  let matched = false;
  every(async () => {
    const sec = (Date.now() - start) / 1000;
    app.querySelector("#clock").textContent = mmss(sec);
    app.querySelector("#m-bar").style.setProperty("--p", `${Math.min(100, (sec / BOT_TIMEOUT) * 100)}%`);
    if (polling || matched) return;
    polling = true;
    try {
      const st = await api.queueStatus();
      if (st.state === "searching") {
        app.querySelector("#m-sub").textContent = `레이팅 ${state.profile.rating} ± ${st.ratingWindow ?? 50} 범위에서 찾는 중 · 대기 ${st.queueSize ?? 1}명`;
      } else if (st.state === "matched") {
        matched = true;
        app.querySelector("#m-title").textContent = st.bot ? "봇과 매칭되었습니다" : "상대를 찾았습니다!";
        app.querySelector("#m-sub").textContent = `${st.opponentNickname} · 레이팅 ${st.opponentRating}`;
        state.match = await api.currentMatch();
        setTimeout(() => { if (state.screen === "matching") go("connect"); }, 1000);
      } else if (st.state === "none") {
        await api.joinQueue();
      }
    } catch (ex) { toast(ex.message, "error"); } finally { polling = false; }
  }, 500);
  app.querySelector('[data-act="cancel"]').addEventListener("click", async () => {
    clearTimers();
    try { await api.leaveQueue(); } catch { /* 이미 빠짐 */ }
    go("lobby");
  });
}

// ───────────────────────── 4. 매칭 완료 · Unity 연결 ─────────────────────────
function renderConnect() {
  if (!state.match) return go("lobby");
  setWorld("vs");
  const playStart = Date.now();
  const draw = () => {
    const m = state.match;
    const playing = m.status === "PLAYING";
    app.innerHTML = layout(`
      ${brand()}
      <h1 class="title-2">${playing ? "대전 중" : "매칭 완료"}</h1>
      <div class="vs">
        <div class="side-card you"><span><span class="s">YOU</span><br><span class="nm">${esc(m.me.nickname)}</span></span><span class="rt"><b>${m.me.rating}</b>${tierOf(m.me.rating)}</span></div>
        <div class="vs-mid">VS</div>
        <div class="side-card foe"><span><span class="s">ENEMY${m.opponent.bot ? " · BOT" : ""}</span><br><span class="nm">${esc(m.opponent.nickname)}</span></span><span class="rt"><b>${m.opponent.rating}</b>${tierOf(m.opponent.rating)}</span></div>
      </div>
      <div class="room"><span>게임 방 <b>${esc(m.roomCode)}</b></span><span>매치 <b>#${m.matchId}</b></span></div>
      <div class="notice ${playing ? "ok" : ""}">${icon("vr")}<div>${playing
        ? `<b>Unity 게임 연결됨 · <span class="num" id="play-clock">00:00</span></b><p>Unity 게임에서 대전하고 있습니다. 끝나면 결과가 여기에 나옵니다.</p>`
        : `<b>Unity 게임 접속을 기다리는 중</b><p>Quest / PC 의 Unity 게임에서 같은 계정으로 로그인하면 이 방으로 들어갑니다.${m.opponent.bot ? "" : " 두 사람이 모두 들어오면 대전이 시작됩니다."}</p>`}</div></div>
      <div class="row">
        ${state.hasUnityBuild ? `<button class="btn go sm2" data-act="open">${icon("play")}Unity 게임 열기</button>` : ""}
        <button class="btn sm" data-act="copy">${icon("copy")}연결 정보 복사</button>
        ${playing ? "" : `<button class="btn sm danger" data-act="leave">매치 취소</button>`}
      </div>
      <div class="spacer"></div>
      ${steps(playing ? 5 : 4, playing ? 0 : 5)}
      ${LOCAL ? `<div class="dev"><span>Unity 없이 시험:</span>${playing ? "" : `<button class="btn sm" data-dev="join">Unity 접속한 것처럼</button>`}<button class="btn sm" data-dev="WIN">승리 보고</button><button class="btn sm" data-dev="LOSE">패배 보고</button></div>` : ""}
    `);
  };
  draw();
  let last = state.match.status;
  let polling = false;
  every(async () => {
    const clock = app.querySelector("#play-clock");
    if (clock) clock.textContent = mmss((Date.now() - playStart) / 1000);
    if (polling) return;
    polling = true;
    try {
      const cur = await api.currentMatch();
      if (!cur || cur.matchId !== state.match.matchId || cur.status === "CANCELLED") { toast("매치가 취소되었습니다.", "error"); return go("lobby"); }
      state.match = cur;
      if (cur.result) return go("result");
      if (cur.status !== last) { last = cur.status; draw(); if (cur.status === "PLAYING") toast("Unity 게임 연결됨 · 대전 시작!", "ok"); }
    } catch { /* 다음 주기 */ } finally { polling = false; }
  }, 1000);
  app.addEventListener("click", onConnectClick);
}
async function onConnectClick(e) {
  if (state.screen !== "connect") return;
  const b = e.target.closest("[data-act],[data-dev]");
  if (!b) return;
  const m = state.match;
  try {
    if (b.dataset.act === "copy") {
      await navigator.clipboard.writeText(JSON.stringify({ server: location.origin, matchId: m.matchId, roomCode: m.roomCode, player: m.me.nickname, opponent: m.opponent.nickname }, null, 2));
      toast("연결 정보를 복사했습니다.", "ok");
    }
    if (b.dataset.act === "leave") { await api.matchLeave(m.matchId); go("lobby"); }
    if (b.dataset.act === "open") openUnity();
    if (b.dataset.dev === "join") { state.match = await api.matchJoin(m.matchId); go("connect"); }
    if (b.dataset.dev === "WIN" || b.dataset.dev === "LOSE") {
      state.match = await api.matchResult(m.matchId, { result: b.dataset.dev, durationSec: 240, casts: 18 });
      go("result");
    }
  } catch (ex) { toast(ex.message, "error"); }
}
function openUnity() {
  const m = state.match;
  const box = document.createElement("div");
  box.className = "unity-frame";
  box.innerHTML = `<iframe title="War of Wizards Unity" src="game/index.html?matchId=${m.matchId}&room=${encodeURIComponent(m.roomCode)}" allow="autoplay; fullscreen; xr-spatial-tracking; camera; microphone"></iframe><button class="btn">${icon("x")}닫기</button>`;
  box.querySelector("button").addEventListener("click", () => box.remove());
  document.body.appendChild(box);
}

// ───────────────────────── 5. 결과 ─────────────────────────
async function renderResult() {
  const m = state.match;
  if (!m?.result) return go("lobby");
  setWorld("vs");
  try { state.profile = await api.getProfile(state.user.id); } catch { /* 이전 값 */ }
  const win = m.result === "WIN";
  const d = m.ratingDelta ?? 0;
  app.innerHTML = layout(`
    ${brand()}
    <p class="lede">랭킹전 · VS ${esc(m.opponent.nickname)}${m.opponent.bot ? " (봇)" : ""}</p>
    <h1 class="result ${win ? "win" : m.result === "DRAW" ? "" : "lose"}">${win ? "승리" : m.result === "DRAW" ? "무승부" : "패배"}</h1>
    <p><span class="delta ${d > 0 ? "up" : d < 0 ? "down" : ""}">${d > 0 ? "+" : ""}${d}</span> <span class="lede">레이팅 → <b class="num" style="font-size:1.3rem;color:#fff">${state.profile?.rating ?? ""}</b></span></p>
    <div class="spacer"></div>
    ${steps(5)}
    <button class="btn go block" data-act="again">다시 게임 시작</button>
    <button class="btn block" data-act="lobby">로비로</button>
  `);
  app.querySelector('[data-act="again"]').addEventListener("click", async (e) => {
    try { await busy(e.currentTarget, () => api.joinQueue(), "대기열 등록 중…"); go("matching"); } catch (ex) { toast(ex.message, "error"); }
  });
  app.querySelector('[data-act="lobby"]').addEventListener("click", () => go("lobby"));
}

// ───────────────────────── 이동 · 시작 ─────────────────────────
const SCREENS = { title: renderTitle, lobby: renderLobby, matching: renderMatching, connect: renderConnect, result: renderResult };
function go(screen) {
  clearTimers();
  app.removeEventListener("click", onLobbyClick);
  app.removeEventListener("click", onConnectClick);
  state.screen = screen;
  every(checkServer, 10000);
  SCREENS[screen]();
}

async function checkServer() {
  try { await api.health(); state.server = true; } catch { state.server = false; }
  const chip = document.getElementById("server-chip");
  if (chip) chip.innerHTML = serverChip();
}

async function enter() {
  state.user = state.user || (await api.getSession());
  if (!state.user) return go("title");
  const [profile, deck] = await Promise.all([api.getProfile(state.user.id), api.loadDeck(state.user.id)]);
  Object.assign(state, { profile, deck });
  const cur = await api.currentMatch().catch(() => null);   // 진행 중인 게임 방이 있으면 이어서
  if (cur && (cur.status === "MATCHED" || cur.status === "PLAYING")) { state.match = cur; return go("connect"); }
  go("lobby");
}

async function boot() {
  buildWorld();
  if (!api) { app.innerHTML = `<p style="padding:40px">app/wow-server-api.js 를 불러오지 못했습니다.</p>`; return; }
  await checkServer();
  fetch("game/index.html", { cache: "no-store" }).then((r) => { state.hasUnityBuild = r.ok; }).catch(() => {});
  try { state.user = await api.getSession(); } catch { state.user = null; }
  if (state.user) { try { return await enter(); } catch (ex) { toast(ex.message, "error"); } }
  go("title");
}

boot();
