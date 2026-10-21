// 1. 로그인 / 회원가입 — 왼쪽: 게임 소개(마법사 1:1 · 룬 그리기 방법), 오른쪽: 입력 카드
import { api, state, loadPlayer, go, toast, busy, icon, duelHTML, skHTML, runeHTML, GOOGLE_G } from "../core.js";

const NICK = /^[A-Za-z0-9가-힣_]{2,12}$/;

function artHTML() {
  return `<section class="auth-art" aria-label="War of Wizards 소개">
    <div>
      <h1 class="logo">WAR OF<br><b>WIZARDS</b></h1>
      <p class="lead">손으로 룬을 그리면 마법이 나가는 VR 마법사 1:1 대전. 유닛을 밀어 상대 넥서스를 먼저 무너뜨리세요.</p>
    </div>
    <div style="height:min(30vh,260px);margin:18px 0">${duelHTML()}</div>
    <div class="stack" style="gap:14px">
      <ol class="how">
        <li class="on"><span class="n">01</span><b>룬 그리기</b><span>핀치한 채 허공에</span></li>
        <li><span class="n">02</span><b>준비</b><span>주먹을 쥔다</span></li>
        <li><span class="n">03</span><b>조준</b><span>검지로 열 지정</span></li>
        <li><span class="n">04</span><b>발동</b><span>마법진이 터진다</span></li>
      </ol>
      <div class="pairs">${["S03", "S06", "H02"].map((id) => `<span class="pair-mini">${runeHTML(id, 40)}${icon("arrow")}${skHTML(id, 40)}</span>`).join("")}</div>
    </div>
  </section>`;
}

function render(root, mode) {
  const signup = mode === "signup";
  root.innerHTML = `<div class="auth">${artHTML()}
    <section class="auth-form"><form class="auth-card" novalidate>
      <h1 style="margin:0 0 4px">${signup ? "마법사 등록" : "로그인"}</h1>
      <p class="muted" style="margin-bottom:22px">${signup ? "새 계정을 만들고 전장에 참여하세요" : "마법사 계정으로 로그인"}</p>
      ${signup ? `
        <div class="field"><label for="nick">마법사 이름 (닉네임)</label><input class="input" id="nick" maxlength="12" placeholder="2~12자 · 한글 · 영문 · 숫자 · _" autocomplete="nickname"><span class="hint" id="nick-hint">2~12자, 한글·영문·숫자·_ 사용 가능</span></div>` : ""}
      <div class="field"><label for="email">이메일</label><div class="input-wrap">${icon("at")}<input class="input" id="email" type="email" placeholder="wizard@example.com" autocomplete="email"></div></div>
      <div class="field"><label for="pw">비밀번호</label><div class="input-wrap">${icon("lock")}<input class="input" id="pw" type="password" placeholder="6자 이상" autocomplete="${signup ? "new-password" : "current-password"}">
        <button type="button" class="btn ghost sm icon-only end" data-act="peek" aria-label="비밀번호 보기">${icon("eye")}</button></div></div>
      ${signup ? `
        <div class="field"><label for="pw2">비밀번호 확인</label><div class="input-wrap">${icon("lock")}<input class="input" id="pw2" type="password" autocomplete="new-password"></div></div>
        <div class="stack" style="gap:8px;margin-top:16px">
          <label class="check"><input type="checkbox" id="terms"><span><span class="req">[필수]</span> 이용약관 및 개인정보처리방침에 동의합니다.</span></label>
          <label class="check"><input type="checkbox" id="mkt"><span>[선택] 마케팅 정보 수신에 동의합니다.</span></label>
        </div>` : `<div style="display:flex;justify-content:flex-end;margin-top:8px"><button type="button" class="btn text" data-act="reset">비밀번호를 잊으셨나요?</button></div>`}
      <p class="err" id="err" role="alert" style="margin-top:10px"></p>
      <button class="btn play lg block" type="submit">${signup ? "마법사 등록하기" : "로그인"}</button>
      ${signup ? "" : `
        <div class="or">또는</div>
        <button type="button" class="btn block" data-act="google">${GOOGLE_G}Google 계정으로 계속하기</button>
        <div class="stack" style="gap:8px;margin-top:10px">
          <div style="display:flex;gap:8px"><input class="input" id="gnick" maxlength="12" placeholder="게스트 이름 (2~12자)" aria-label="게스트 이름"><button type="button" class="btn info" data-act="guest">게스트 입장</button></div>
          <button type="button" class="btn ghost block" data-act="demo">데모 계정으로 바로 시작</button>
        </div>`}
      <p class="switch-line">${signup ? `이미 계정이 있으신가요? <a href="#/login">로그인</a>` : `계정이 없으신가요? <a href="#/signup">회원가입</a>`}</p>
      <p class="hint" style="text-align:center;margin-top:14px"><span class="dot ${state.server ? "on" : "bad"}" style="display:inline-block;margin-right:6px"></span>${state.server ? "파이썬 게임 서버 연결됨" : "서버 꺼짐"}</p>
    </form></section></div>`;

  const form = root.querySelector("form");
  const $ = (id) => form.querySelector(`#${id}`);
  const err = (m) => { $("err").textContent = m || ""; };
  const enter = async (user) => { await loadPlayer(user); go("lobby"); };

  if (signup) {
    $("nick").addEventListener("blur", async () => {
      const nick = $("nick").value.trim();
      if (!nick) return;
      const hint = $("nick-hint");
      if (!NICK.test(nick)) { hint.className = "err"; hint.textContent = "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다."; return; }
      try {
        const ok = await api.isNicknameAvailable(nick);
        hint.className = ok ? "ok-text" : "err";
        hint.textContent = ok ? `'${nick}' 은(는) 쓸 수 있는 이름입니다.` : "이미 사용 중인 닉네임입니다.";
      } catch { /* 가입할 때 다시 확인 */ }
    });
  }

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    err("");
    const email = $("email").value.trim();
    const pw = $("pw").value;
    const btn = form.querySelector('[type="submit"]');
    try {
      if (signup) {
        const nick = $("nick").value.trim();
        if (!NICK.test(nick)) return err("닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.");
        if (!email.includes("@")) return err("올바른 이메일을 입력해주세요.");
        if (pw.length < 6) return err("비밀번호는 6자 이상이어야 합니다.");
        if (pw !== $("pw2").value) return err("비밀번호가 일치하지 않습니다.");
        if (!$("terms").checked) return err("필수 약관에 동의해주세요.");
        await busy(btn, () => api.signUp(email, pw, nick), "계정 생성 중…");
        toast(`${nick} 마법사님, 환영합니다!`, "ok");
        await enter(await api.signIn(email, pw));
      } else {
        if (!email.includes("@")) return err("올바른 이메일 주소를 입력해주세요.");
        if (pw.length < 6) return err("비밀번호는 6자 이상이어야 합니다.");
        await enter(await busy(btn, () => api.signIn(email, pw), "로그인 중…"));
      }
    } catch (ex) { err(ex.message); }
  });

  form.addEventListener("click", async (e) => {
    const b = e.target.closest("[data-act]");
    if (!b) return;
    const a = b.dataset.act;
    err("");
    try {
      if (a === "peek") {
        const pw = $("pw");
        pw.type = pw.type === "password" ? "text" : "password";
        b.setAttribute("aria-label", pw.type === "password" ? "비밀번호 보기" : "비밀번호 숨기기");
      }
      if (a === "demo") await enter(await busy(b, () => api.signIn("demo@wow.dev", "demo1234"), "입장 중…"));
      if (a === "google") await api.signInWithGoogle();
      if (a === "reset") {
        const email = $("email").value.trim();
        if (!email.includes("@")) { err("비밀번호를 재설정할 이메일을 먼저 입력해주세요."); $("email").focus(); return; }
        toast((await api.resetPassword(email)) || "재설정 요청을 접수했습니다.", "ok");
      }
      if (a === "guest") {
        const nick = $("gnick").value.trim();
        if (!nick) { err("게스트 이름을 입력해주세요."); $("gnick").focus(); return; }
        if (!NICK.test(nick)) return err("이름은 2~12자, 한글·영문·숫자·_ 만 쓸 수 있습니다.");
        await enter(await busy(b, () => api.guestLogin(nick), "입장 중…"));
      }
    } catch (ex) { err(ex.message); }
  });
  setTimeout(() => (signup ? $("nick") : $("email")).focus(), 30);
}

export default {
  login: { title: "로그인", render: (root) => render(root, "login") },
  signup: { title: "마법사 등록", render: (root) => render(root, "signup") },
};
