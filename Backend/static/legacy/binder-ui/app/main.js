// War of Wizards — 시작점: 로그인 복구 → 화면 이동(#/이름) → 바인더 틀(왼쪽 책등 + 페이지)
import { state, subscribe, restoreSession, applySettings, currentRoute, go, unread, icon, emblem, tierGem, esc, signOut, trackHolo } from "./core.js";
import auth from "./screens/auth.js";
import lobby from "./screens/lobby.js";
import spellbook from "./screens/spellbook.js";
import friends from "./screens/friends.js";
import room from "./screens/room.js";
import mailbox from "./screens/mailbox.js";
import ranking from "./screens/ranking.js";
import mypage from "./screens/mypage.js";
import settings from "./screens/settings.js";
import queue from "./screens/queue.js";
import countdown from "./screens/countdown.js";
import game from "./screens/game.js";
import result from "./screens/result.js";

const SCREENS = {
  login: auth.login, signup: auth.signup, lobby, spellbook, friends, room, mailbox, ranking, mypage, settings,
  queue, countdown, game, result,
};
const PUBLIC = new Set(["login", "signup"]);
const NAV = [
  ["lobby", "로비", "lobby"],
  ["spellbook", "마법서", "book"],
  ["friends", "친구", "users"],
  ["room", "친선전 룸", "door"],
  ["mailbox", "우편함", "mail"],
  ["ranking", "랭킹", "trophy"],
  ["mypage", "마이페이지", "user"],
  ["settings", "환경설정", "gear"],
];
const DEV_LIST = [
  ["login", "1. 로그인/가입"], ["lobby", "2. 메인 로비"], ["mypage", "3. 마이페이지"], ["settings", "4. 환경설정"],
  ["friends", "5. 친구"], ["room", "6. 친선전 룸"], ["mailbox", "7. 우편/공지"], ["ranking", "8. 랭킹"],
  ["spellbook", "9. 마법서"], ["queue", "10. 매칭 대기"], ["countdown", "11. 카운트다운"], ["game", "12. 게임 화면"],
  ["result", "13. 결과"],
];

const app = document.getElementById("app");
let current = { name: null, screen: null, cleanup: null };
let shellEl = null;
let lastHash = location.hash;

function railHTML(name) {
  const p = state.profile;
  const n = unread();
  return `
    <a class="brand" href="#/lobby">${emblem()}<span><span class="brand-name">War of Wizards</span><br><span class="brand-sub">룬을 그리는 1:1 마법 대결</span></span></a>
    <nav class="nav" aria-label="주 메뉴">
      ${NAV.map(([id, label, ic]) => `<a href="#/${id}" ${id === name ? 'aria-current="page"' : ""}>${icon(ic)}<span class="lbl">${label}</span>${id === "mailbox" && n ? `<span class="badge">${n}</span>` : ""}</a>`).join("")}
    </nav>
    <div class="rail-foot">
      ${p ? `<a class="me-chip" href="#/mypage">${tierGem(p.rating)}<span class="me-name">${esc(p.nickname)}</span><span class="me-rate num">레이팅 ${p.rating}</span></a>` : ""}
      <button class="btn btn-quiet sm" data-act="logout">${icon("logout", "sm")}<span>로그아웃</span></button>
    </div>`;
}

function ensureShell(name) {
  if (!shellEl || !shellEl.isConnected) {
    app.innerHTML = `<div class="shell"><aside class="rail on-vinyl"></aside><main class="page" id="page"><div class="page-inner" id="screen"></div></main></div>`;
    shellEl = app.firstElementChild;
    shellEl.querySelector(".rail").addEventListener("click", (e) => {
      if (e.target.closest('[data-act="logout"]')) signOut();
    });
    trackHolo(shellEl);
  }
  shellEl.querySelector(".rail").innerHTML = railHTML(name);
  return shellEl.querySelector("#screen");
}

async function route() {
  if (!state.ready) return;
  let name = currentRoute();
  if (name === "battle") name = "game";
  if (!SCREENS[name]) name = state.user ? "lobby" : "login";
  if (!state.user && !PUBLIC.has(name)) name = "login";
  if (state.user && PUBLIC.has(name)) name = "lobby";
  if ("#/" + name !== location.hash) history.replaceState(null, "", "#/" + name);  // 이벤트 없이 주소만 고침

  // 떠나기 전에 확인할 화면 (저장 안 한 덱 등)
  if (current.screen?.canLeave && current.name !== name) {
    const ok = await current.screen.canLeave();
    if (!ok) {
      history.replaceState(null, "", lastHash);
      return;
    }
  }
  lastHash = "#/" + name;

  if (current.cleanup) { try { current.cleanup(); } catch (e) { console.warn(e); } }
  const screen = SCREENS[name];
  let root;
  if (screen.shell) {
    root = ensureShell(name);
    document.getElementById("page").scrollTop = 0;
  } else {
    shellEl = null;
    app.innerHTML = `<div id="screen" class="fullscreen" style="height:100%"></div>`;
    root = app.firstElementChild;
    trackHolo(root);
  }
  document.title = screen.title ? `${screen.title} · War of Wizards` : "War of Wizards";
  current = { name, screen, cleanup: screen.render(root) || null };
}

// 책등(메뉴)의 우편 개수 · 내 정보는 상태가 바뀌면 다시 그린다
subscribe((patch) => {
  if (shellEl && shellEl.isConnected && ("profile" in patch || "mails" in patch)) {
    shellEl.querySelector(".rail").innerHTML = railHTML(current.name);
  }
  if ("user" in patch && !state.user && current.name && !PUBLIC.has(current.name)) go("login");
});

function devNav() {
  const host = location.hostname;
  if (!["localhost", "127.0.0.1", "[::1]"].includes(host)) return;
  const box = document.createElement("div");
  box.className = "devnav";
  box.innerHTML = `<div class="devnav-list" hidden>${DEV_LIST.map(([id, label]) => `<button data-go="${id}">${label}</button>`).join("")}</div><button class="btn sm" data-toggle aria-expanded="false">DEV 화면 목록 (F2)</button>`;
  const list = box.querySelector(".devnav-list");
  const toggle = () => {
    list.hidden = !list.hidden;
    box.querySelector("[data-toggle]").setAttribute("aria-expanded", String(!list.hidden));
  };
  box.addEventListener("click", (e) => {
    if (e.target.closest("[data-toggle]")) toggle();
    const id = e.target.closest("[data-go]")?.dataset.go;
    if (id) { list.hidden = true; go(id); }
  });
  window.addEventListener("keydown", (e) => { if (e.key === "F2") { e.preventDefault(); toggle(); } });
  document.body.appendChild(box);
}

async function boot() {
  applySettings();
  app.innerHTML = `<div class="loading-screen"><div style="text-align:center">${emblem()}<p style="margin-top:14px">마법서를 펼치는 중…</p></div></div>`;
  if (!window.WOW_SERVER_API) {
    app.innerHTML = `<div class="loading-screen"><p>wow-server-api.js 를 불러오지 못했습니다.</p></div>`;
    return;
  }
  await restoreSession();
  window.addEventListener("hashchange", route);
  devNav();
  route();
}

boot();
