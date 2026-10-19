// War of Wizards — 시작점: 서버 확인 → 로그인 복구 → 화면 이동(#/이름). 화면 구성은 팀 UI 프로토타입 13개 + 추가 화면.
import { api, state, setState, go, loadPlayer, applySettings, isAdmin, toast } from "./core.js";
import auth from "./pages/auth.js";
import lobby from "./pages/lobby.js";
import mypage from "./pages/mypage.js";
import settings from "./pages/settings.js";
import friends from "./pages/friends.js";
import room from "./pages/room.js";
import mailbox from "./pages/mailbox.js";
import ranking from "./pages/ranking.js";
import spellbook from "./pages/spellbook.js";
import queue from "./pages/queue.js";
import countdown from "./pages/countdown.js";
import battle from "./pages/battle.js";
import result from "./pages/result.js";
import runes from "./pages/runes.js";
import admin from "./pages/admin.js";
import notFound from "./pages/notfound.js";

const PAGES = {
  login: auth.login, signup: auth.signup, lobby, mypage, settings, friends, room, mailbox, ranking, spellbook,
  queue, countdown, battle, result, runes, admin, 404: notFound,
};
const PUBLIC = new Set(["login", "signup", "404"]);
// 프로토타입의 DEV 화면 목록 (발표 · 시험용, 내 컴퓨터에서만)
const DEV = [
  ["login", "1. 로그인/가입"], ["lobby", "2. 메인 로비"], ["mypage", "3. 마이페이지"], ["settings", "4. 환경설정"],
  ["friends", "5. 친구"], ["room", "6. 친선전 룸"], ["mailbox", "7. 우편/공지"], ["ranking", "8. 랭킹"],
  ["spellbook", "9. 마법서"], ["queue", "10. 매칭 대기"], ["countdown", "11. 카운트다운"], ["battle", "12. 전투 HUD"],
  ["result", "13. 결과"], ["runes", "룬 연습"], ["admin", "관리자"],
];
const LOCAL = ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname);
// 게임 몰입 화면에서는 DEV 버튼을 숨긴다 (` 키로 잠깐 꺼내기)
const IMMERSIVE = new Set(["queue", "countdown", "battle"]);

const app = document.getElementById("app");
let current = { name: null, page: null, cleanup: null };
let lastHash = location.hash;

function devBar(name) {
  if (!LOCAL) return;
  let bar = document.querySelector(".dev");
  if (!bar) {
    bar = document.createElement("div");
    bar.className = "dev";
    bar.innerHTML = `<button type="button" data-toggle aria-expanded="false" title="\` 키로 숨기기 · 꺼내기">≡ DEV</button><div class="dev-list" hidden></div>`;
    bar.addEventListener("click", (e) => {
      if (e.target.closest("[data-toggle]")) {
        const list = bar.querySelector(".dev-list");
        list.hidden = !list.hidden;
        bar.querySelector("[data-toggle]").setAttribute("aria-expanded", String(!list.hidden));
      }
      const id = e.target.closest("[data-go]")?.dataset.go;
      if (id) go(id);
    });
    document.body.appendChild(bar);
    window.addEventListener("keydown", (e) => {
      if (e.code !== "Backquote" || e.target.closest("input, textarea")) return;
      bar.classList.toggle("peek");
    });
  }
  bar.classList.toggle("tucked", IMMERSIVE.has(name));
  bar.classList.remove("peek");
  bar.querySelector(".dev-list").innerHTML = DEV.map(([id, label]) => {
    const locked = (!state.user && !PUBLIC.has(id)) || (id === "admin" && !isAdmin());
    return `<button type="button" data-go="${id}" ${id === name ? 'aria-current="true"' : ""} ${locked ? "disabled" : ""}>${label}</button>`;
  }).join("");
}

async function route() {
  if (!state.ready) return;
  let name = (location.hash.replace(/^#\/?/, "") || "").split("?")[0];
  if (name === "") name = state.user ? "lobby" : "login";
  if (!PAGES[name] || (name === "admin" && !isAdmin())) name = "404";
  if (!state.user && !PUBLIC.has(name)) name = "login";
  if (state.user && (name === "login" || name === "signup")) name = "lobby";
  if ("#/" + name !== location.hash) history.replaceState(null, "", "#/" + name);

  if (current.page?.canLeave && current.name !== name && !(await current.page.canLeave())) {
    history.replaceState(null, "", lastHash);
    return;
  }
  lastHash = "#/" + name;
  if (current.cleanup) { try { current.cleanup(); } catch (e) { console.warn(e); } }
  const page = PAGES[name];
  app.innerHTML = "";
  const root = document.createElement("div");
  root.className = "screen";
  app.appendChild(root);
  document.title = page.title ? `${page.title} · War of Wizards` : "War of Wizards";
  current = { name, page, cleanup: page.render(root) || null };
  devBar(name);
}

async function boot() {
  applySettings();
  if (!api) { app.innerHTML = `<div class="loading">app/wow-server-api.js 를 불러오지 못했습니다</div>`; return; }
  app.innerHTML = `<div class="loading">불러오는 중…</div>`;
  try { await api.health(); setState({ server: true }); } catch { setState({ server: false }); }
  if (!state.server) { location.replace("50x.html?from=client"); return; }
  fetch("game/index.html", { cache: "no-store" }).then((r) => setState({ hasUnityBuild: r.ok })).catch(() => {});
  try {
    const user = await api.getSession();
    if (user) await loadPlayer(user);
  } catch (e) { toast(e.message, "error"); }
  setState({ ready: true });
  window.addEventListener("hashchange", route);
  route();
}

boot();
