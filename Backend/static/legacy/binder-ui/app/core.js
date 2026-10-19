// War of Wizards — 공용: 상태 · 서버 호출 · 화면 이동 · 그림 조각(아이콘, 카드)
import { SKILL, KINDS, RULES, DEFAULT_SETTINGS, deckProblem, tierOf } from "./data.js";

export const api = window.WOW_SERVER_API;

// ───────────────────────── 상태 ─────────────────────────
const SETTINGS_KEY = "wow.settings.v1";

function loadSettings() {
  try {
    const raw = localStorage.getItem(SETTINGS_KEY);
    if (raw) return { ...DEFAULT_SETTINGS, ...JSON.parse(raw) };
  } catch { /* 저장소를 못 쓰면 기본값 */ }
  return { ...DEFAULT_SETTINGS };
}

export const state = {
  ready: false,
  user: null,          // { id: playerId, email }
  profile: null,       // { id, nickname, rating, wins, losses }
  deck: [],
  mails: [],
  online: [],
  settings: loadSettings(),
  matchMode: "bot",
  opponent: null,      // { name, rating, isBot, playerId? }
  room: null,          // { code, role: host|guest, hostName, server }
  lastMatch: null,
};

const listeners = new Set();
export function setState(patch) {
  Object.assign(state, patch);
  listeners.forEach((fn) => fn(patch));
}
export function subscribe(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export const unread = () => state.mails.filter((m) => !m.read).length;

// ───────────────────────── 계정 · 데이터 ─────────────────────────
let stopPresence = null;

async function loadPlayer(user) {
  const [profile, deck, mails] = await Promise.all([api.getProfile(user.id), api.loadDeck(user.id), api.listMail(user.id)]);
  setState({ user, profile, deck, mails });
  if (stopPresence) stopPresence();
  stopPresence = api.subscribePresence(user.id, (online) => setState({ online }));
}

export async function restoreSession() {
  try {
    const user = await api.getSession();
    if (user) await loadPlayer(user);
  } catch (e) {
    console.warn("세션 복구 실패", e);
  }
  setState({ ready: true });
}

export async function signIn(email, password) {
  const user = await api.signIn(email, password);
  await loadPlayer(user);
}

export async function signUp(email, password, nickname) {
  await api.signUp(email, password, nickname);
  await signIn(email, password);
}

export async function signOut() {
  await api.signOut();
  if (stopPresence) stopPresence();
  stopPresence = null;
  setState({ user: null, profile: null, deck: [], mails: [], online: [], opponent: null, room: null, lastMatch: null });
  go("login");
}

export async function refreshProfile() {
  if (state.user) setState({ profile: await api.getProfile(state.user.id) });
}
export async function refreshMails() {
  if (state.user) setState({ mails: await api.listMail(state.user.id) });
}
export async function markMailRead(item) {
  if (!state.user || item.read) return;
  setState({ mails: state.mails.map((m) => (m.id === item.id ? { ...m, read: true } : m)) });
  try {
    await api.markMailRead(state.user.id, item);
  } catch (e) {
    toast(e.message, "error");
  }
}
export async function saveDeck(deck) {
  const problem = deckProblem(deck);
  if (problem) throw new Error(problem);
  await api.saveDeck(state.user.id, deck);
  setState({ deck: [...deck] });
}

export function updateSettings(patch) {
  const settings = { ...state.settings, ...patch };
  try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); } catch { /* 저장 불가 */ }
  setState({ settings });
  applySettings();
}
export function resetSettings() {
  updateSettings({ ...DEFAULT_SETTINGS });
}
export function applySettings() {
  document.documentElement.classList.toggle("ui-large", !!state.settings.uiLarge);
  document.documentElement.classList.toggle("colorblind", !!state.settings.colorblind);
}

// 대전 시작 · 끝 (전투 자체는 Unity, 웹은 시작 정보와 결과 기록만)
let matchMeta = null;
export function beginMatch(mode, opponent) {
  matchMeta = { mode, opponent: opponent.name, startedAt: Date.now(), recorded: false };
  setState({ matchMode: mode, opponent, lastMatch: null });
}
export function finishMatch(outcome) {
  if (!matchMeta || matchMeta.recorded) return;
  matchMeta.recorded = true;
  const meta = matchMeta;
  const record = { ...outcome, mode: meta.mode, opponent: meta.opponent };
  setState({ lastMatch: record });
  if (!state.user) return;
  api.recordMatch(state.user.id, {
    mode: meta.mode,
    result: outcome.result,
    opponent: meta.opponent,
    durationSec: outcome.durationSec,
    casts: outcome.stats?.casts ?? 0,
  })
    .then(async (saved) => {
      setState({ lastMatch: { ...state.lastMatch, record: saved } });
      await refreshProfile();
    })
    .catch((e) => setState({ lastMatch: { ...state.lastMatch, saveError: e.message } }));
}

// ───────────────────────── 화면 이동 (#/이름) ─────────────────────────
export function go(name) {
  const target = "#/" + name;
  if (location.hash === target) window.dispatchEvent(new HashChangeEvent("hashchange"));
  else location.hash = target;
}
export const currentRoute = () => (location.hash.replace(/^#\/?/, "") || "").split("?")[0];

// ───────────────────────── 알림 · 확인 창 ─────────────────────────
let toastSeq = 0;
export function toast(msg, tone = "info") {
  let box = document.querySelector(".toasts");
  if (!box) {
    box = document.createElement("div");
    box.className = "toasts";
    box.setAttribute("aria-live", "polite");
    document.body.appendChild(box);
  }
  const el = document.createElement("button");
  el.type = "button";
  el.className = `toast ${tone}`;
  el.dataset.id = String(++toastSeq);
  el.textContent = msg;
  el.addEventListener("click", () => el.remove());
  box.appendChild(el);
  while (box.children.length > 4) box.firstElementChild.remove();
  setTimeout(() => el.remove(), 3800);
}

export function confirmDialog(title, text, okLabel = "확인", danger = false) {
  return new Promise((resolve) => {
    const d = document.createElement("dialog");
    d.className = "confirm";
    d.innerHTML = `<h3>${esc(title)}</h3><p>${esc(text)}</p>
      <div class="actions"><button class="btn btn-quiet" value="no">취소</button>
      <button class="btn ${danger ? "btn-danger" : "btn-plate"}" value="ok">${esc(okLabel)}</button></div>`;
    document.body.appendChild(d);
    d.addEventListener("click", (e) => {
      const v = e.target.closest("button")?.value;
      if (v) d.close(v);
    });
    d.addEventListener("close", () => {
      resolve(d.returnValue === "ok");
      d.remove();
    });
    d.showModal();
  });
}

// ───────────────────────── 글자 · 숫자 ─────────────────────────
export function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}
export function fmtTime(sec) {
  const t = Math.max(0, Math.floor(sec));
  return `${Math.floor(t / 60)}:${String(t % 60).padStart(2, "0")}`;
}
export function winRate(w, l) {
  const n = w + l;
  return n === 0 ? 0 : Math.round((w / n) * 100);
}
export function timeAgo(iso) {
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 60) return "방금 전";
  if (s < 3600) return `${Math.floor(s / 60)}분 전`;
  if (s < 86400) return `${Math.floor(s / 3600)}시간 전`;
  return `${Math.floor(s / 86400)}일 전`;
}
export async function busy(button, fn) {
  if (button) {
    button.disabled = true;
    button.dataset.label = button.innerHTML;
    button.innerHTML = `<span class="spinner" aria-hidden="true"></span>${button.dataset.busy || "처리 중…"}`;
  }
  try {
    return await fn();
  } finally {
    if (button && button.isConnected) {
      button.disabled = false;
      button.innerHTML = button.dataset.label;
    }
  }
}

// ───────────────────────── 아이콘 (선 굵기 1.8, 24칸) ─────────────────────────
const PATHS = {
  lobby: '<path d="M4 10.5 12 4l8 6.5V20a1 1 0 0 1-1 1h-4.5v-6h-5v6H5a1 1 0 0 1-1-1z"/>',
  book: '<path d="M5 4.5A1.5 1.5 0 0 1 6.5 3H19v15H6.5A1.5 1.5 0 0 0 5 19.5z"/><path d="M5 19.5A1.5 1.5 0 0 0 6.5 21H19v-3"/><path d="M9 7.5h6M9 11h4"/>',
  users: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c.6-3.6 3.3-5.5 6.5-5.5s5.9 1.9 6.5 5.5"/><path d="M15.5 4.8a3.5 3.5 0 0 1 0 6.4M17.5 14.8c2.1.6 3.6 2.4 4 5.2"/>',
  mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3.5 6.5 8.5 6.5 8.5-6.5"/>',
  trophy: '<path d="M8 4h8v5a4 4 0 0 1-8 0z"/><path d="M8 6H4.5v1.5A3.5 3.5 0 0 0 8 11M16 6h3.5v1.5A3.5 3.5 0 0 1 16 11"/><path d="M12 13v4M8.5 21h7M10 17h4l.5 4h-5z"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c.8-4.2 4-6.5 8-6.5s7.2 2.3 8 6.5"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M12 2.5v3M12 18.5v3M21.5 12h-3M5.5 12h-3M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1M18.7 18.7l-2.1-2.1M7.4 7.4 5.3 5.3"/>',
  logout: '<path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4"/><path d="M10 16l-4-4 4-4M6 12h10"/>',
  swords: '<path d="M14.5 4H20v5.5L9 20.5 3.5 15z"/><path d="m5 13 6 6M16 8l-2-2"/>',
  door: '<path d="M6 21V4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5V21"/><path d="M3.5 21h17M14 12.5v.5"/>',
  bot: '<rect x="4" y="8" width="16" height="11" rx="3"/><path d="M12 4v4M9 13h.01M15 13h.01M9.5 16.5h5"/><circle cx="12" cy="3.5" r="1"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5.5A1.5 1.5 0 0 0 14.5 4h-9A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16H8"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  left: '<path d="m14.5 6-6 6 6 6"/>',
  right: '<path d="m9.5 6 6 6-6 6"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  eye: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/>',
  eyeOff: '<path d="M3 3l18 18M10.6 6.1A9.8 9.8 0 0 1 12 6c6 0 9.5 6 9.5 6a17.6 17.6 0 0 1-3.3 3.9M6.2 7.6C3.9 9.3 2.5 12 2.5 12S6 18 12 18a9.4 9.4 0 0 0 4.1-.9"/><path d="M9.9 10a3 3 0 0 0 4.1 4.1"/>',
  at: '<circle cx="12" cy="12" r="4"/><path d="M16 8v5a2.5 2.5 0 0 0 5 0v-1a9 9 0 1 0-3.5 7.1"/>',
  lock: '<rect x="4.5" y="10.5" width="15" height="10" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/>',
  flag: '<path d="M5 21V4M5 4h11l-2 4 2 4H5"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  link: '<path d="M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1 1"/><path d="M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1-1"/>',
  send: '<path d="M21 3 10 14M21 3l-7 18-4-7-7-4z"/>',
  gift: '<rect x="3.5" y="8" width="17" height="4" rx="1"/><path d="M5 12v8h14v-8M12 8v12M12 8c-1.5-3.5-5.5-4-5.5-1.5S10 8 12 8zm0 0c1.5-3.5 5.5-4 5.5-1.5S14 8 12 8z"/>',
  arrow: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  pinch: '<path d="M8 13V6.5a1.5 1.5 0 0 1 3 0V11"/><path d="M11 10.5a1.5 1.5 0 0 1 3 0v1M14 11a1.5 1.5 0 0 1 3 0v2.5a6.5 6.5 0 0 1-6.5 6.5h-.3a5.2 5.2 0 0 1-4.4-2.4L4 14.5a1.5 1.5 0 0 1 2.4-1.8L8 14"/><circle cx="17.5" cy="5.5" r="1.5"/>',
  scan: '<path d="M4 8V5.5A1.5 1.5 0 0 1 5.5 4H8M16 4h2.5A1.5 1.5 0 0 1 20 5.5V8M20 16v2.5a1.5 1.5 0 0 1-1.5 1.5H16M8 20H5.5A1.5 1.5 0 0 1 4 18.5V16"/><path d="M8 12h8"/>',
  fist: '<path d="M7 11V8.5a1.5 1.5 0 0 1 3 0V11M10 9a1.5 1.5 0 0 1 3 0v2M13 9.5a1.5 1.5 0 0 1 3 0V12M16 11a1.5 1.5 0 0 1 3 0v2.5a6.5 6.5 0 0 1-6.5 6.5h-1A5.5 5.5 0 0 1 6 14.5V11z"/>',
  point: '<path d="M10 13V4.5a1.5 1.5 0 0 1 3 0V11M13 10a1.5 1.5 0 0 1 3 0v1.5M16 11a1.5 1.5 0 0 1 3 0v2.5a6.5 6.5 0 0 1-6.5 6.5h-.8a5.5 5.5 0 0 1-4.6-2.5L5 15a1.5 1.5 0 0 1 2.4-1.8L10 15"/>',
  spark: '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M18.4 5.6l-2.8 2.8M8.4 15.6l-2.8 2.8"/><circle cx="12" cy="12" r="2.5"/>',
  ready: '<circle cx="12" cy="12" r="8.5"/><path d="m8.5 12.5 2.5 2.5 4.5-5"/>',
  trash: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>',
  refresh: '<path d="M20 11a8 8 0 0 0-14.6-4.5L4 8M4 4v4h4M4 13a8 8 0 0 0 14.6 4.5L20 16M20 20v-4h-4"/>',
  star: '<path d="m12 3.5 2.6 5.3 5.9.9-4.3 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8L3.5 9.7l5.9-.9z"/>',
  bell: '<path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z"/><path d="M10 20.5a2 2 0 0 0 4 0"/>',
  pin: '<circle cx="12" cy="10" r="3"/><path d="M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z"/>',
};
export function icon(name, cls = "") {
  return `<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">${PATHS[name] || ""}</svg>`;
}

export function emblem(cls = "") {
  return `<svg class="emblem ${cls}" viewBox="0 0 100 100" aria-hidden="true" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">
    <circle cx="50" cy="50" r="46" stroke-width="3"/>
    <circle cx="50" cy="50" r="37" stroke-width="1.6" stroke-dasharray="3 5"/>
    <path d="M24 34 36 70 50 46 64 70 76 34" stroke-width="5.5"/>
    <path d="M50 4v8M50 88v8M4 50h8M88 50h8" stroke-width="3"/>
  </svg>`;
}

export const GOOGLE_G = `<svg viewBox="0 0 24 24" aria-hidden="true"><path fill="#4285F4" d="M22.6 12.3c0-.8-.1-1.6-.2-2.3H12v4.4h5.9a5 5 0 0 1-2.2 3.3v2.7h3.6c2.1-1.9 3.3-4.8 3.3-8.1z"/><path fill="#34A853" d="M12 23c3 0 5.5-1 7.3-2.7l-3.6-2.7c-1 .7-2.2 1.1-3.7 1.1-2.9 0-5.3-1.9-6.2-4.5H2.1v2.8A11 11 0 0 0 12 23z"/><path fill="#FBBC05" d="M5.8 14.2a6.6 6.6 0 0 1 0-4.3V7.1H2.1a11 11 0 0 0 0 9.9z"/><path fill="#EA4335" d="M12 5.4c1.6 0 3.1.6 4.2 1.7l3.2-3.2A11 11 0 0 0 2.1 7.1l3.7 2.8C6.7 7.3 9.1 5.4 12 5.4z"/></svg>`;

// 등급 보석 (희귀도 표시)
export function tierGem(rating, cls = "") {
  const t = tierOf(rating);
  const fill = { legend: "url(#gemHolo)", epic: "#c99a2e", rare: "#1fa3c0", uncommon: "#5875d6", common: "#9aa4b8" }[t.key];
  return `<svg class="tier-gem ${cls}" viewBox="0 0 30 30" role="img" aria-label="${t.name}">
    <defs><linearGradient id="gemHolo" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ff9bd2"/><stop offset=".35" stop-color="#a5c8ff"/><stop offset=".7" stop-color="#8ff2d8"/><stop offset="1" stop-color="#ffe795"/></linearGradient></defs>
    <path d="M15 2 27 11 15 28 3 11z" fill="${fill}" stroke="rgba(0,0,0,.35)" stroke-width="1"/>
    <path d="M3 11h24M10 11l5 17 5-17M10 11l5-9 5 9" fill="none" stroke="rgba(255,255,255,.55)" stroke-width="1"/>
  </svg>`;
}

// ───────────────────────── 주문 카드 ─────────────────────────
// opts: size(xs|sm|md|lg|xl), flip("hover"|"on"|false), clickable, inDeck, disabled, stamp, uses, extraClass, attrs
export function cardHTML(id, opts = {}) {
  const s = SKILL[id];
  if (!s) return "";
  const kind = KINDS[s.kind];
  const cd = s.high ? RULES.cooldown.high : RULES.cooldown.normal;
  const cls = [
    "wcard",
    opts.size || "md",
    s.high ? "holo" : "",
    opts.flip === "hover" ? "flip-hover" : "",
    opts.flip === "on" ? "is-flipped" : "",
    opts.clickable ? "clickable" : "",
    opts.inDeck ? "in-deck" : "",
    opts.disabled ? "is-disabled" : "",
    opts.extraClass || "",
  ].join(" ");
  const pips = s.high
    ? `<span class="hp-pips" aria-label="게임당 ${RULES.highUsesPerGame}회">${"<i></i>".repeat(RULES.highUsesPerGame)}</span>`
    : `<span>${kind.label}</span>`;
  return `<div class="${cls}" data-id="${s.id}" style="--rune:url('${s.runeImg}')" ${opts.attrs || ""}>
    ${opts.stamp ? `<span class="stamp">${opts.stamp}</span>` : ""}
    <div class="wcard-inner">
      <div class="face front"><div class="face-body">
        <div class="card-top"><span class="code">${s.id}</span><span>${cd}s</span></div>
        <div class="card-art"><span class="rune-ink"></span></div>
        <div class="card-name">${s.name}</div>
        <div class="card-foot"><span>룬</span>${pips}</div>
      </div></div>
      <div class="face back"><div class="face-body">
        <div class="card-top"><span class="code">${s.id}</span><span>${kind.label}</span></div>
        <div class="card-art"><img src="${s.circle}" alt="" loading="lazy" draggable="false"></div>
        <div class="card-name">${s.name}</div>
        <div class="card-foot"><span>마법진</span><span>${s.high ? "고위력" : "일반"}</span></div>
      </div></div>
    </div>
  </div>`;
}

export function cardBackHTML(size = "sm") {
  return `<div class="wcard ${size}" aria-hidden="true"><div class="card-cover">${emblem()}</div></div>`;
}

export function playerCardHTML(p, opts = {}) {
  if (!p) return "";
  const t = tierOf(p.rating);
  const games = (p.wins ?? 0) + (p.losses ?? 0);
  return `<div class="pcard ${opts.frame ?? t.frame} ${opts.cls || ""}">
    <div class="pcard-body">
      <div class="pcard-top"><span>${esc(opts.label || "WIZARD")}</span><span class="num">${opts.rank ? `#${opts.rank}` : ""}</span></div>
      <div class="pcard-name">${esc(p.nickname ?? p.name ?? "?")}</div>
      <div class="pcard-tier">${tierGem(p.rating)}<span>${t.name}</span></div>
      ${opts.compact ? `<div class="rating-big num" style="margin-top:10px">${p.rating ?? "-"}</div>` : `
      <div class="pcard-stats">
        <div><b>${p.rating ?? "-"}</b><span>레이팅</span></div>
        <div><b>${p.wins ?? 0}<small>승</small> ${p.losses ?? 0}<small>패</small></b><span>전적 ${games}판</span></div>
        <div><b>${winRate(p.wins ?? 0, p.losses ?? 0)}%</b><span>승률</span></div>
      </div>`}
    </div>
  </div>`;
}

// 홀로 카드 광택이 포인터를 따라가게
export function trackHolo(root) {
  const onMove = (e) => {
    const card = e.target.closest?.(".wcard.holo, .pcard.holo");
    if (!card) return;
    const r = card.getBoundingClientRect();
    card.style.setProperty("--mx", `${((e.clientX - r.left) / r.width) * 100}%`);
    card.style.setProperty("--my", `${((e.clientY - r.top) / r.height) * 100}%`);
  };
  root.addEventListener("pointermove", onMove);
  return () => root.removeEventListener("pointermove", onMove);
}
