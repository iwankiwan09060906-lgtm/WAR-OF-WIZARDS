// War of Wizards — 공용: 게임 규칙 · 상태 · 서버 · 화면 이동 · 그림 조각
export const api = window.WOW_SERVER_API;

// ───────────────────────── 스킬 19종 (서버 wow_server.py 와 같은 내용) ─────────────────────────
export const KINDS = {
  projectile: { label: "투사체", aim: "열 지정", needsAim: true },
  area: { label: "범위", aim: "범위 지정 (열+깊이)", needsAim: true },
  beam: { label: "관통 빔", aim: "열 지정", needsAim: true },
  buff: { label: "버프", aim: "즉시 발동", needsAim: false },
  summon: { label: "소환", aim: "소환 라인 (아군)", needsAim: true },
  install: { label: "설치", aim: "설치 구역 (타워·넥서스 앞)", needsAim: true },
  global: { label: "전역", aim: "즉시 발동", needsAim: false },
};
const RAW = [
  ["S01", "연막", "projectile", "적중 시 상대 명중률 감소 및 시야 외곽 안개 부여", "눈"],
  ["S02", "독 장판", "area", "조준 지점에 독병을 던져 3열을 가로지르는 독 장판 (머물수록 피해 증가)", "삼각형 + X"],
  ["S03", "파이어볼", "area", "소범위 집중 즉발 피해", "왕관"],
  ["S04", "화살 세례", "area", "광범위 다수 분산 피해", "세 갈래 화살"],
  ["S05", "토네이도", "projectile", "레인을 쓸고 지나가는 회오리 — 닿은 적 유닛을 뒤로 밀어내고 피해", "소용돌이"],
  ["S06", "번개", "area", "지정 위치의 세로로 긴 범위(한 레인)에 낙뢰 3번", "번개"],
  ["S07", "총난사", "projectile", "열 전방 유닛부터 순차 연사 타격", "가로줄 5개"],
  ["S08", "방어 쉴드", "buff", "피해 흡수 쉴드 생성 (피격 없을 시 HP 회복)", "방패"],
  ["S09", "타워 버프", "buff", "본인 타워 방어력/공격력 일시 강화 (타워 파괴 시 비활성화)", "#"],
  ["S10", "집중", "buff", "다음 공격 1회의 명중률 및 공격력 대폭 증가", "위쪽 화살표"],
  ["S11", "도끼 전사", "summon", "근접 공격형 소환수 소환", "X"],
  ["S12", "드래곤", "summon", "공중 비행 원거리 화염 소환수 소환", "육각별"],
  ["S13", "터렛", "install", "적 유닛 요격용 고정 포탑 설치 (진영당 1기)", "I"],
  ["H01", "프리즈", "global", "적 전체 미니언 · 소환수 · 설치물 · 타워 7초간 정지 + 피해", "원 안 모래시계"],
  ["H02", "레이저 빔", "beam", "0.5초 예고선 후 4초간 유지되는 빔으로 열 전체 관통 타격 (유닛 및 끝의 플레이어)", "별표"],
  ["H03", "흡혈", "beam", "조준 레인의 적 플레이어 HP를 흡수 (못 맞히면 레인의 유닛 · 설치물에서 흡수)", "심장 박동 하트"],
  ["H04", "반사 결계", "buff", "9초간 상대 플레이어의 스킬 피해 무효화 및 100% 반사", "원 + 위 화살표"],
  ["H05", "분노", "area", "지정 위치에 3열 분노 장판 — 들어온 아군 유닛 공격력 · 공격 속도 · 이동 속도 · 방어력 대폭 증가", "화난 얼굴"],
  ["H06", "소환탑", "install", "주기적으로 소형 근접 소환수를 생성하는 탑 설치", "집"],
];
const ASSETS = new URL("assets/skills/", document.baseURI).href;
export const SKILLS = RAW.map(([id, name, kind, effect, rune]) => ({
  id, name, kind, effect, rune, high: id[0] === "H",
  circle: `${ASSETS}circle/${id}.png`, runeImg: `${ASSETS}rune/${id}.png`, runeLine: `${ASSETS}rune_line/${id}.png`,
}));
export const SKILL = Object.fromEntries(SKILLS.map((s) => [s.id, s]));
export const RULES = { deckSize: 8, highLimit: 2, highUses: 3, cooldown: { normal: 5, high: 20 }, botTimeout: 15, countdown: 3, maxHp: 1000, aimTimeout: 5 };
export const MODE = { ranked: "랭킹전", friendly: "친선전", bot: "봇 연습전", matchmaking: "랭킹전" };
export const TIPS = [
  "적 타워가 살아있는 동안에는 상대 플레이어에게 20%의 피해만 들어갑니다.",
  "플레이어 스킬은 타워·넥서스를 공격할 수 없어요. 유닛으로 구조물을 밀어붙이세요.",
  "고위력 스킬은 게임당 3회까지만 쓸 수 있습니다. 결정적인 순간에 아껴두세요.",
  "왼손을 옆으로 휘두르면 좌/중/우 열을 한 칸씩 순간이동합니다.",
  "적 타워를 부수면 공격력이 영구 +20%, 넥서스를 부수면 +40% 강해집니다.",
];

export function deckProblem(deck) {
  if (deck.length !== RULES.deckSize) return `덱은 정확히 8장이어야 합니다. (현재 ${deck.length}장)`;
  if (new Set(deck).size !== deck.length) return "같은 스킬을 중복해서 넣을 수 없습니다.";
  if (deck.some((id) => !SKILL[id])) return "존재하지 않는 스킬이 포함되어 있습니다.";
  if (deck.filter((id) => SKILL[id].high).length > RULES.highLimit) return "고위력 스킬은 덱에 최대 2장까지 넣을 수 있습니다.";
  return null;
}
export const tierOf = (r = 1000) => (r >= 1500 ? "대마법사" : r >= 1300 ? "마법사" : r >= 1100 ? "숙련 마법사" : r >= 900 ? "견습 마법사" : "입문자");

// ───────────────────────── 상태 ─────────────────────────
const SETTINGS_KEY = "wow.settings.v1";
export const DEFAULT_SETTINGS = {
  camSensitivity: 65, comfortVignette: true, snapTurning: false, sfx: 80, bgm: 45, spatialAudio: true,
  gestureSensitivity: 70, holdFrames: 15, haptics: true, subtitles: false, uiLarge: false, colorblind: false,
  players: 2,    // 대전 인원: 2 = 1:1, 3 · 4 = 개인전 (팀 없이 나 빼고 모두 적)
};
function loadSettings() {
  try { return { ...DEFAULT_SETTINGS, ...JSON.parse(localStorage.getItem(SETTINGS_KEY) || "{}") }; } catch { return { ...DEFAULT_SETTINGS }; }
}
export const state = {
  ready: false, server: null, user: null, profile: null, deck: [], mails: [], online: [],
  settings: loadSettings(), room: null,
  matchCtx: null,     // { mode, opponent: { name, rating, bot }, matchId?, roomCode? }
  lastMatch: null,    // 결과 화면용
  hasUnityBuild: false,
};
const listeners = new Set();
export function setState(patch) { Object.assign(state, patch); listeners.forEach((fn) => fn(patch)); }
export function subscribe(fn) { listeners.add(fn); return () => listeners.delete(fn); }
export const unread = () => state.mails.filter((m) => !m.read).length;
export const isAdmin = () => state.user?.role === "ROLE_ADMIN";

let stopPresence = null;
export async function loadPlayer(user) {
  const [profile, deck, mails] = await Promise.all([api.getProfile(user.id), api.loadDeck(user.id), api.listMail(user.id).catch(() => [])]);
  setState({ user, profile, deck, mails });
  if (stopPresence) stopPresence();
  stopPresence = api.subscribePresence(user.id, (online) => setState({ online }));
}
export async function refreshProfile() { if (state.user) setState({ profile: await api.getProfile(state.user.id) }); }
export async function refreshMails() { if (state.user) setState({ mails: await api.listMail(state.user.id) }); }
export async function markMailRead(item) {
  if (!state.user || item.read) return;
  setState({ mails: state.mails.map((m) => (m.id === item.id ? { ...m, read: true } : m)) });
  try { await api.markMailRead(state.user.id, item); } catch (e) { toast(e.message, "error"); }
}
export async function saveDeck(deck) {
  const p = deckProblem(deck);
  if (p) throw new Error(p);
  await api.saveDeck(state.user.id, deck);
  setState({ deck: [...deck] });
}
export async function signOut() {
  try { await api.signOut(); } catch { /* 서버가 꺼져 있어도 로그아웃 */ }
  if (stopPresence) stopPresence();
  stopPresence = null;
  setState({ user: null, profile: null, deck: [], mails: [], online: [], room: null, matchCtx: null, lastMatch: null });
  go("login");
}
export function updateSettings(patch) {
  const settings = { ...state.settings, ...patch };
  try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); } catch { /* 저장 불가 */ }
  setState({ settings });
  applySettings();
}
export function applySettings() {
  document.documentElement.classList.toggle("ui-large", !!state.settings.uiLarge);
  document.documentElement.classList.toggle("colorblind", !!state.settings.colorblind);
}

// 대전 시작 · 끝. 랭킹전은 서버 게임 방(matchId)에 결과를 보고하고, 친선전 · 봇 연습은 전적만 기록한다.
export function beginMatch(mode, opponent, extra = {}) {
  // gameId = 한 판 구분 (AI 학습: 이 판의 시전 기록에 결과 이름표를 붙인다)
  const gameId = extra.matchId ? `m${extra.matchId}` : `g${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
  setState({ matchCtx: { mode, opponent, startedAt: Date.now(), done: false, maxPlayers: 2, opponents: [], gameId, ...extra }, lastMatch: null });
}
// 서버 게임 방(매칭 결과 · /api/match/current)으로 시작 — 1:1 · 개인전 공용. 개인전 상대 이름은 "A · B · C"
export function beginServerMatch(mode, m) {
  const opponent = m.opponent
    ? { name: m.opponent.nickname, rating: m.opponent.rating, bot: m.opponent.bot }
    : { name: m.opponentNickname, rating: m.opponentRating, bot: m.bot !== false };
  beginMatch(mode, opponent, { matchId: m.matchId, roomCode: m.roomCode, maxPlayers: m.maxPlayers || 2, seat: m.seat,
    opponents: m.opponents || [] });
}
export const playersLabel = (n) => (n > 2 ? `${n}인 개인전` : "1 : 1");
export async function finishMatch({ result, reason = "", durationSec, stats = {} }) {
  const ctx = state.matchCtx;
  if (!ctx || ctx.done) return;
  ctx.done = true;
  const record = { result, reason, durationSec, stats, mode: ctx.mode, opponent: ctx.opponent };
  setState({ lastMatch: record });
  try {
    let saved;
    if (ctx.matchId) {
      const m = await api.matchResult(ctx.matchId, { result: result.toUpperCase(), durationSec, casts: stats.casts || 0, gameId: ctx.gameId,
        place: result === "win" ? 1 : undefined });
      saved = { ratingDelta: m.ratingDelta, matchId: m.matchId };
    } else {
      saved = await api.recordMatch(state.user.id, { mode: ctx.mode, result, opponent: ctx.opponent.name, durationSec, casts: stats.casts || 0, gameId: ctx.gameId });
    }
    await refreshProfile();
    setState({ lastMatch: { ...state.lastMatch, record: { ...saved, newRating: state.profile?.rating } } });
  } catch (e) {
    setState({ lastMatch: { ...state.lastMatch, saveError: e.message } });
  }
}

// ───────────────────────── 화면 이동 (#/이름) ─────────────────────────
export function go(name) {
  const target = "#/" + name;
  if (location.hash === target) window.dispatchEvent(new HashChangeEvent("hashchange"));
  else location.hash = target;
}

// ───────────────────────── 알림 · 확인 · 바쁨 ─────────────────────────
export function toast(msg, tone = "") {
  let box = document.querySelector(".toasts");
  if (!box) { box = document.createElement("div"); box.className = "toasts"; box.setAttribute("aria-live", "polite"); document.body.appendChild(box); }
  const el = document.createElement("button");
  el.type = "button";
  el.className = `toast ${tone}`;
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
    d.innerHTML = `<h3>${esc(title)}</h3><p>${esc(text)}</p><div class="actions"><button class="btn ghost" value="no">취소</button><button class="btn ${danger ? "danger" : "play"}" value="ok">${esc(okLabel)}</button></div>`;
    document.body.appendChild(d);
    d.addEventListener("click", (e) => { const v = e.target.closest("button")?.value; if (v) d.close(v); });
    d.addEventListener("close", () => { resolve(d.returnValue === "ok"); d.remove(); });
    d.showModal();
  });
}
export async function busy(btn, fn, label = "잠시만요…") {
  if (!btn) return fn();
  const html = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = `<span class="spin" aria-hidden="true"></span>${label}`;
  try { return await fn(); } finally { if (btn.isConnected) { btn.disabled = false; btn.innerHTML = html; } }
}

// ───────────────────────── 글자 · 숫자 ─────────────────────────
export const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
export const mmss = (sec) => { const t = Math.max(0, Math.floor(sec)); return `${Math.floor(t / 60)}:${String(t % 60).padStart(2, "0")}`; };
export const winRate = (w, l) => (w + l ? Math.round((w / (w + l)) * 100) : 0);
export function timeAgo(iso) {
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (!isFinite(s)) return "";
  if (s < 60) return "방금 전";
  if (s < 3600) return `${Math.floor(s / 60)}분 전`;
  if (s < 86400) return `${Math.floor(s / 3600)}시간 전`;
  return `${Math.floor(s / 86400)}일 전`;
}

// ───────────────────────── 아이콘 ─────────────────────────
const PATHS = {
  book: '<path d="M5 4.5A1.5 1.5 0 0 1 6.5 3H19v15H6.5A1.5 1.5 0 0 0 5 19.5z"/><path d="M5 19.5A1.5 1.5 0 0 0 6.5 21H19v-3M9 7.5h6M9 11h4"/>',
  users: '<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c.6-3.6 3.3-5.5 6.5-5.5s5.9 1.9 6.5 5.5M15.5 4.8a3.5 3.5 0 0 1 0 6.4M17.5 14.8c2.1.6 3.6 2.4 4 5.2"/>',
  mail: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3.5 6.5 8.5 6.5 8.5-6.5"/>',
  trophy: '<path d="M8 4h8v5a4 4 0 0 1-8 0zM8 6H4.5v1.5A3.5 3.5 0 0 0 8 11M16 6h3.5v1.5A3.5 3.5 0 0 1 16 11M12 13v4M8.5 21h7M10 17h4l.5 4h-5z"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c.8-4.2 4-6.5 8-6.5s7.2 2.3 8 6.5"/>',
  gear: '<circle cx="12" cy="12" r="3"/><path d="M12 2.5v3M12 18.5v3M21.5 12h-3M5.5 12h-3M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1M18.7 18.7l-2.1-2.1M7.4 7.4 5.3 5.3"/>',
  logout: '<path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4M10 16l-4-4 4-4M6 12h10"/>',
  door: '<path d="M6 21V4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5V21M3.5 21h17M14 12.5v.5"/>',
  bot: '<rect x="4" y="8" width="16" height="11" rx="3"/><path d="M12 4v4M9 13h.01M15 13h.01M9.5 16.5h5"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="2"/><path d="M16 8V5.5A1.5 1.5 0 0 0 14.5 4h-9A1.5 1.5 0 0 0 4 5.5v9A1.5 1.5 0 0 0 5.5 16H8"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  check: '<path d="m5 12.5 4.5 4.5L19 7.5"/>',
  left: '<path d="m14.5 6-6 6 6 6"/>',
  right: '<path d="m9.5 6 6 6-6 6"/>',
  send: '<path d="M21 3 10 14M21 3l-7 18-4-7-7-4z"/>',
  gift: '<rect x="3.5" y="8" width="17" height="4" rx="1"/><path d="M5 12v8h14v-8M12 8v12M12 8c-1.5-3.5-5.5-4-5.5-1.5S10 8 12 8zm0 0c1.5-3.5 5.5-4 5.5-1.5S14 8 12 8z"/>',
  arrow: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  back: '<path d="M19 12H5M11 6l-6 6 6 6"/>',
  pen: '<path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16z"/><path d="m13.5 6.5 4 4"/>',
  pinch: '<path d="M8 13V6.5a1.5 1.5 0 0 1 3 0V11M11 10.5a1.5 1.5 0 0 1 3 0v1M14 11a1.5 1.5 0 0 1 3 0v2.5a6.5 6.5 0 0 1-6.5 6.5h-.3a5.2 5.2 0 0 1-4.4-2.4L4 14.5a1.5 1.5 0 0 1 2.4-1.8L8 14"/>',
  fist: '<path d="M7 11V8.5a1.5 1.5 0 0 1 3 0V11M10 9a1.5 1.5 0 0 1 3 0v2M13 9.5a1.5 1.5 0 0 1 3 0V12M16 11a1.5 1.5 0 0 1 3 0v2.5a6.5 6.5 0 0 1-6.5 6.5h-1A5.5 5.5 0 0 1 6 14.5V11z"/>',
  point: '<path d="M10 13V4.5a1.5 1.5 0 0 1 3 0V11M13 10a1.5 1.5 0 0 1 3 0v1.5M16 11a1.5 1.5 0 0 1 3 0v2.5a6.5 6.5 0 0 1-6.5 6.5h-.8a5.5 5.5 0 0 1-4.6-2.5L5 15a1.5 1.5 0 0 1 2.4-1.8L10 15"/>',
  scan: '<path d="M4 8V5.5A1.5 1.5 0 0 1 5.5 4H8M16 4h2.5A1.5 1.5 0 0 1 20 5.5V8M20 16v2.5a1.5 1.5 0 0 1-1.5 1.5H16M8 20H5.5A1.5 1.5 0 0 1 4 18.5V16M8 12h8"/>',
  spark: '<path d="M12 3v4M12 17v4M3 12h4M17 12h4M5.6 5.6l2.8 2.8M15.6 15.6l2.8 2.8M18.4 5.6l-2.8 2.8M8.4 15.6l-2.8 2.8"/><circle cx="12" cy="12" r="2.5"/>',
  vr: '<path d="M3.5 8.5A2.5 2.5 0 0 1 6 6h12a2.5 2.5 0 0 1 2.5 2.5v6A2.5 2.5 0 0 1 18 17h-3.2l-1.6-2.4a1.5 1.5 0 0 0-2.4 0L9.2 17H6a2.5 2.5 0 0 1-3.5-2.5z"/><circle cx="8" cy="11.5" r="1.6"/><circle cx="16" cy="11.5" r="1.6"/>',
  play: '<path d="M7 5v14l11-7z"/>',
  flag: '<path d="M5 21V4M5 4h11l-2 4 2 4H5"/>',
  bell: '<path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15zM10 20.5a2 2 0 0 0 4 0"/>',
  trash: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>',
  refresh: '<path d="M20 11a8 8 0 0 0-14.6-4.5L4 8M4 4v4h4M4 13a8 8 0 0 0 14.6 4.5L20 16M20 20v-4h-4"/>',
  lock: '<rect x="4.5" y="10.5" width="15" height="10" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/>',
  at: '<circle cx="12" cy="12" r="4"/><path d="M16 8v5a2.5 2.5 0 0 0 5 0v-1a9 9 0 1 0-3.5 7.1"/>',
  eye: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/>',
  chart: '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
};
export const icon = (n, cls = "") => `<svg class="icon ${cls}" viewBox="0 0 24 24" aria-hidden="true">${PATHS[n] || ""}</svg>`;
export const GOOGLE_G = `<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true"><path fill="#4285F4" d="M22.6 12.3c0-.8-.1-1.6-.2-2.3H12v4.4h5.9a5 5 0 0 1-2.2 3.3v2.7h3.6c2.1-1.9 3.3-4.8 3.3-8.1z"/><path fill="#34A853" d="M12 23c3 0 5.5-1 7.3-2.7l-3.6-2.7c-1 .7-2.2 1.1-3.7 1.1-2.9 0-5.3-1.9-6.2-4.5H2.1v2.8A11 11 0 0 0 12 23z"/><path fill="#FBBC05" d="M5.8 14.2a6.6 6.6 0 0 1 0-4.3V7.1H2.1a11 11 0 0 0 0 9.9z"/><path fill="#EA4335" d="M12 5.4c1.6 0 3.1.6 4.2 1.7l3.2-3.2A11 11 0 0 0 2.1 7.1l3.7 2.8C6.7 7.3 9.1 5.4 12 5.4z"/></svg>`;

// ───────────────────────── 화면 머리글 (프로토타입 구성) ─────────────────────────
// 왼쪽: 게임 이름(누르면 로비) | 화면 이름, 오른쪽: 화면별 버튼 + 로비로
export function headerHTML(title, { right = "", back = true } = {}) {
  return `<header class="ph"><div class="ph-left"><a class="wordmark" href="#/lobby" aria-label="War of Wizards 로비로">WAR OF <b>WIZARDS</b></a><h1>${title}</h1></div>
    <div class="ph-right">${right}${back ? `<a class="btn ghost sm" href="#/lobby">${icon("back")}로비</a>` : ""}</div></header>`;
}

// 스킬 아이콘 (마법진) · 룬 그림
export function skHTML(id, size = 46, cls = "") {
  const s = SKILL[id];
  if (!s) return "";
  return `<span class="sk ${s.high ? "high" : ""} ${cls}" style="--s:${size}px" title="${s.id} ${s.name}"><img src="${s.circle}" alt="" draggable="false"></span>`;
}
export function runeHTML(id, size = 46, line = false) {
  const s = SKILL[id];
  if (!s) return "";
  return `<span class="rune-tile" style="--s:${size}px;--rune:url('${line ? s.runeLine : s.runeImg}')" title="${s.name} 룬: ${s.rune}"><i></i></span>`;
}

// ───────────────────────── 마법사 (파랑 = 나, 빨강 = 상대) ─────────────────────────
const PALETTE = {
  you: { robe1: "#2f7fc9", robe2: "#122a52", back: "#0c1c38", arm: "#1d4a7e", hat1: "#2a62a8", hat2: "#0e2247", brim: "#0a1730", glow: "#5fd4ff", eye: "#c6f1ff" },
  foe: { robe1: "#c4405a", robe2: "#4a1220", back: "#330b16", arm: "#7c2236", hat1: "#a32f47", hat2: "#3f0f1d", brim: "#2c0912", glow: "#ff7d95", eye: "#ffd7df" },
};
let wizSeq = 0;
export function wizardSVG(side = "you") {
  const c = PALETTE[side];
  const id = `wz${++wizSeq}`;
  const flip = side === "foe" ? ' transform="translate(240 0) scale(-1 1)"' : "";
  return `<svg viewBox="-10 -6 260 366" role="img" aria-label="${side === "you" ? "파란 마법사 (나)" : "빨간 마법사 (상대)"}">
    <defs>
      <linearGradient id="${id}r" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${c.robe1}"/><stop offset="1" stop-color="${c.robe2}"/></linearGradient>
      <linearGradient id="${id}h" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${c.hat1}"/><stop offset="1" stop-color="${c.hat2}"/></linearGradient>
      <radialGradient id="${id}o"><stop offset="0" stop-color="#fff"/><stop offset=".3" stop-color="${c.glow}"/><stop offset="1" stop-color="${c.glow}" stop-opacity="0"/></radialGradient>
    </defs>
    <g${flip}>
      <ellipse cx="120" cy="350" rx="76" ry="9" fill="rgba(0,0,0,.45)"/>
      <path d="M58 92 L65 92 L57 348 L50 348 Z" fill="#4e3a2c"/>
      <path d="M62 96 C44 90 40 64 58 56 C74 50 84 66 74 76" stroke="#5f4532" stroke-width="6" fill="none" stroke-linecap="round"/>
      <circle class="orb" cx="60" cy="70" r="30" fill="url(#${id}o)"/><circle cx="60" cy="70" r="6.5" fill="#fff"/>
      <path d="M94 126 C72 176 54 256 42 340 L198 340 C188 258 168 178 148 126 Z" fill="${c.back}"/>
      <path d="M98 122 C86 170 74 252 66 340 L94 331 L121 343 L149 331 L178 340 C170 252 158 170 146 122 Q122 112 98 122 Z" fill="url(#${id}r)"/>
      <path d="M121 128 L123 338" stroke="rgba(0,0,0,.22)" stroke-width="3"/>
      <path d="M66 340 L94 331 L121 343 L149 331 L178 340" stroke="${c.glow}" stroke-width="3" opacity=".75" fill="none" stroke-linejoin="round"/>
      <path d="M146 124 C158 172 170 252 178 336" stroke="${c.glow}" stroke-width="2" opacity=".5" fill="none"/>
      <path d="M90 194 Q122 204 154 194 L156 206 Q122 216 88 206 Z" fill="#cdb27a"/>
      <path d="M104 128 C90 140 76 156 66 170 L77 180 C88 166 100 156 112 148 Z" fill="${c.arm}"/><circle cx="67" cy="175" r="8" fill="#e2c3a6"/>
      <path d="M140 128 C160 130 178 116 190 96 L202 105 C190 130 168 150 144 154 Z" fill="url(#${id}r)"/>
      <circle cx="198" cy="99" r="8.5" fill="#e2c3a6"/><path d="M203 93 L210 86" stroke="#e2c3a6" stroke-width="4" stroke-linecap="round"/>
      <path d="M210 88 C232 74 238 52 222 44 C206 36 194 56 214 64 C226 68 236 60 232 50" stroke="${c.glow}" stroke-width="10" opacity=".2" fill="none" stroke-linecap="round"/>
      <path class="trail" d="M210 88 C232 74 238 52 222 44 C206 36 194 56 214 64 C226 68 236 60 232 50" stroke="${c.glow}" stroke-width="3.5" fill="none" stroke-linecap="round"/>
      <ellipse cx="121" cy="104" rx="22" ry="24" fill="#10141c"/>
      <circle cx="127" cy="103" r="2.6" fill="${c.eye}"/><circle cx="138" cy="102" r="2.6" fill="${c.eye}"/>
      <path d="M137 107 L147 113 L137 115 Z" fill="#e2c3a6"/>
      <path d="M110 112 C106 140 118 162 134 176 C134 152 146 134 146 112 Q128 122 110 112 Z" fill="#d9dde3"/>
      <ellipse cx="118" cy="86" rx="56" ry="11" fill="${c.brim}"/>
      <path d="M84 86 L154 86 C138 64 124 40 116 24 C110 12 98 6 82 10 C96 16 104 28 104 42 C102 60 94 74 84 86 Z" fill="url(#${id}h)"/>
      <path d="M88 79 Q120 73 152 79 L154 86 Q120 80 86 86 Z" fill="#cdb27a"/>
      <path d="M154 86 C138 64 124 40 116 24" stroke="${c.glow}" stroke-width="1.6" opacity=".55" fill="none"/>
    </g>
  </svg>`;
}
// 마법사 1:1 (foe 없으면 실루엣). st: idle · search · win · lose
export function duelHTML({ foe = true, st = "idle" } = {}) {
  return `<div class="duel ${st === "search" ? "searching" : ""}">
    <div class="wiz you ${st === "lose" ? "lost" : ""}">${wizardSVG("you")}</div>
    <span class="vs">VS</span>
    <div class="wiz foe ${foe ? "" : "unknown"} ${st === "win" ? "lost" : ""}">${wizardSVG("foe")}</div>
  </div>`;
}
