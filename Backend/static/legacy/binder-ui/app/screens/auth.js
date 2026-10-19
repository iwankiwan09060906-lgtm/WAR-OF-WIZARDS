// 로그인 · 회원가입 — 바인더 앞표지(왼쪽) + 펼친 페이지(오른쪽)
import { api, signIn, signUp, go, toast, icon, emblem, cardHTML, busy, GOOGLE_G } from "../core.js";

const FAN = ["S06", "H02", "S01"];

function coverHTML() {
  return `<section class="cover on-vinyl" aria-label="War of Wizards">
    <div class="cover-brand">${emblem()}</div>
    <div style="position:relative">
      <h1 class="wordmark">War of<br>Wizards</h1>
      <p class="cover-copy">룬을 그리면 마법이 됩니다. 손으로 룬을 그려 상대 마법사와 1:1로 겨루세요.</p>
    </div>
    <div class="fan">${FAN.map((id) => cardHTML(id, { size: "lg" })).join("")}</div>
    <div class="cover-legend"><span><b>19</b>종 주문 카드</span><span><b>8</b>장으로 덱 편성</span><span>앞면 <b>룬</b> → 뒷면 <b>마법진</b></span></div>
  </section>`;
}

// 표지의 카드 세 장이 차례로 룬 → 마법진으로 뒤집힌다
function startFan(root) {
  const cards = [...root.querySelectorAll(".fan .wcard")];
  let i = 0;
  const timer = setInterval(() => {
    const c = cards[i % cards.length];
    c.classList.toggle("is-flipped");
    i++;
  }, 1300);
  return () => clearInterval(timer);
}

function bindPasswordToggle(root) {
  root.querySelectorAll("[data-toggle-pw]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const input = root.querySelector(`#${btn.dataset.togglePw}`);
      const show = input.type === "password";
      input.type = show ? "text" : "password";
      btn.innerHTML = icon(show ? "eyeOff" : "eye", "sm");
      btn.setAttribute("aria-label", show ? "비밀번호 숨기기" : "비밀번호 보기");
    });
  });
}

function setError(root, id, msg) {
  const input = root.querySelector(`#${id}`);
  const out = root.querySelector(`#${id}-err`);
  if (input) input.setAttribute("aria-invalid", msg ? "true" : "false");
  if (out) out.textContent = msg || "";
}

const login = {
  title: "로그인",
  shell: false,
  render(root) {
    root.innerHTML = `<div class="auth">${coverHTML()}
      <section class="auth-page">
        <form class="auth-form" novalidate>
          <h2 class="page-title">로그인</h2>
          <p class="muted">마법사 계정으로 로그인</p>
          <div style="margin-top:22px">
            <div class="field">
              <label class="label" for="email">이메일</label>
              <div class="input-wrap">${icon("at")}<input class="input" id="email" type="email" autocomplete="email" placeholder="wizard@example.com" required></div>
              <span class="field-error" id="email-err"></span>
            </div>
            <div class="field">
              <label class="label" for="pw">비밀번호</label>
              <div class="input-wrap">${icon("lock")}<input class="input" id="pw" type="password" autocomplete="current-password" placeholder="6자 이상" required>
                <button type="button" class="btn btn-quiet btn-icon sm input-btn" data-toggle-pw="pw" aria-label="비밀번호 보기">${icon("eye", "sm")}</button></div>
              <span class="field-error" id="pw-err"></span>
            </div>
            <div style="display:flex;justify-content:flex-end;margin-top:6px">
              <button type="button" class="btn btn-quiet sm" data-act="reset">비밀번호를 잊으셨나요?</button>
            </div>
          </div>
          <div class="notice-box" role="alert" hidden id="form-err" style="margin-top:12px"></div>
          <button class="btn btn-plate lg block" type="submit" data-busy="주문 시전 중…" style="margin-top:16px">로그인</button>
          <div class="or">또는</div>
          <button type="button" class="btn btn-google block" data-act="google">${GOOGLE_G}Google 계정으로 계속하기</button>
          <button type="button" class="btn btn-quiet block" data-act="demo" style="margin-top:8px">데모 계정으로 바로 시작 (demo@wow.dev)</button>
          <p class="auth-switch">계정이 없으신가요? <button type="button" data-act="signup">회원가입</button></p>
        </form>
      </section></div>`;
    bindPasswordToggle(root);
    const form = root.querySelector("form");
    const formErr = root.querySelector("#form-err");
    const showFormErr = (msg) => { formErr.hidden = !msg; formErr.textContent = msg || ""; };

    const validate = () => {
      const email = root.querySelector("#email").value.trim();
      const pw = root.querySelector("#pw").value;
      let ok = true;
      setError(root, "email", email.includes("@") ? "" : ((ok = false), "올바른 이메일 주소를 입력해주세요."));
      setError(root, "pw", pw.length >= 6 ? "" : ((ok = false), "비밀번호는 6자 이상이어야 합니다."));
      return ok ? { email, pw } : null;
    };

    form.addEventListener("submit", async (e) => {
      e.preventDefault();
      const v = validate();
      if (!v) return;
      showFormErr("");
      try {
        await busy(form.querySelector('[type="submit"]'), () => signIn(v.email, v.pw));
        go("lobby");
      } catch (err) {
        showFormErr(err.message);
      }
    });

    root.addEventListener("click", async (e) => {
      const act = e.target.closest("[data-act]")?.dataset.act;
      if (!act) return;
      if (act === "signup") go("signup");
      if (act === "demo") {
        showFormErr("");
        try {
          await busy(e.target.closest("button"), () => signIn("demo@wow.dev", "demo1234"));
          go("lobby");
        } catch (err) {
          showFormErr(err.message);
        }
      }
      if (act === "google") {
        try {
          await api.signInWithGoogle();
        } catch (err) {
          toast(err.message, "error");
        }
      }
      if (act === "reset") {
        const email = root.querySelector("#email").value.trim();
        if (!email.includes("@")) {
          setError(root, "email", "비밀번호를 재설정할 이메일을 먼저 입력해주세요.");
          root.querySelector("#email").focus();
          return;
        }
        try {
          const msg = await api.resetPassword(email);
          toast(msg || "비밀번호 재설정 요청을 접수했습니다.", "success");
        } catch (err) {
          toast(err.message, "error");
        }
      }
    });
    return startFan(root);
  },
};

const signup = {
  title: "마법사 등록",
  shell: false,
  render(root) {
    root.innerHTML = `<div class="auth">${coverHTML()}
      <section class="auth-page">
        <form class="auth-form" novalidate>
          <h2 class="page-title">마법사 등록</h2>
          <p class="muted">새 계정을 만들고 전장에 참여하세요</p>
          <div style="margin-top:22px">
            <div class="field">
              <label class="label" for="nick">마법사 이름 (닉네임)</label>
              <input class="input" id="nick" autocomplete="nickname" maxlength="12" placeholder="예: 번개마법사">
              <span class="field-hint" id="nick-hint">2~12자, 한글·영문·숫자·_ 사용 가능</span>
              <span class="field-error" id="nick-err"></span>
            </div>
            <div class="field">
              <label class="label" for="email">이메일</label>
              <div class="input-wrap">${icon("at")}<input class="input" id="email" type="email" autocomplete="email" placeholder="wizard@example.com"></div>
              <span class="field-error" id="email-err"></span>
            </div>
            <div class="field">
              <label class="label" for="pw">비밀번호</label>
              <div class="input-wrap">${icon("lock")}<input class="input" id="pw" type="password" autocomplete="new-password" placeholder="6자 이상">
                <button type="button" class="btn btn-quiet btn-icon sm input-btn" data-toggle-pw="pw" aria-label="비밀번호 보기">${icon("eye", "sm")}</button></div>
              <span class="field-error" id="pw-err"></span>
            </div>
            <div class="field">
              <label class="label" for="pw2">비밀번호 확인</label>
              <div class="input-wrap">${icon("lock")}<input class="input" id="pw2" type="password" autocomplete="new-password"></div>
              <span class="field-error" id="pw2-err"></span>
            </div>
            <div class="field" style="gap:10px;margin-top:18px">
              <label class="check"><input type="checkbox" id="terms"><span><b class="req">[필수]</b> 이용약관 및 개인정보처리방침에 동의합니다.</span></label>
              <label class="check"><input type="checkbox" id="mkt"><span>[선택] 마케팅 정보 수신에 동의합니다.</span></label>
              <span class="field-error" id="terms-err"></span>
            </div>
          </div>
          <div class="notice-box" role="alert" hidden id="form-err" style="margin-top:12px"></div>
          <button class="btn btn-plate lg block" type="submit" data-busy="계정 생성 중…" style="margin-top:16px">마법사 등록하기</button>
          <p class="auth-switch">이미 계정이 있으신가요? <button type="button" data-act="login">로그인</button></p>
        </form>
      </section></div>`;
    bindPasswordToggle(root);
    const $ = (id) => root.querySelector(`#${id}`);
    const formErr = $("form-err");
    const NICK = /^[A-Za-z0-9가-힣_]{2,12}$/;

    $("nick").addEventListener("blur", async () => {
      const nick = $("nick").value.trim();
      if (!nick) return;
      if (!NICK.test(nick)) return setError(root, "nick", "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.");
      try {
        const ok = await api.isNicknameAvailable(nick);
        setError(root, "nick", ok ? "" : "이미 사용 중인 닉네임입니다.");
        $("nick-hint").className = ok ? "field-ok" : "field-hint";
        $("nick-hint").textContent = ok ? `'${nick}' 은(는) 쓸 수 있는 이름입니다.` : "2~12자, 한글·영문·숫자·_ 사용 가능";
      } catch { /* 서버 확인은 가입 때 다시 */ }
    });

    root.querySelector("form").addEventListener("submit", async (e) => {
      e.preventDefault();
      const nick = $("nick").value.trim();
      const email = $("email").value.trim();
      const pw = $("pw").value;
      let ok = true;
      const fail = (id, msg) => { setError(root, id, msg); ok = false; };
      NICK.test(nick) ? setError(root, "nick", "") : fail("nick", "닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.");
      email.includes("@") ? setError(root, "email", "") : fail("email", "올바른 이메일을 입력해주세요.");
      pw.length >= 6 ? setError(root, "pw", "") : fail("pw", "비밀번호는 6자 이상이어야 합니다.");
      pw === $("pw2").value ? setError(root, "pw2", "") : fail("pw2", "비밀번호가 일치하지 않습니다.");
      $("terms").checked ? setError(root, "terms", "") : fail("terms", "필수 약관에 동의해주세요.");
      if (!ok) return;
      formErr.hidden = true;
      try {
        await busy(root.querySelector('[type="submit"]'), () => signUp(email, pw, nick));
        toast(`${nick} 마법사님, 환영합니다!`, "success");
        go("lobby");
      } catch (err) {
        formErr.hidden = false;
        formErr.textContent = err.message;
      }
    });
    root.addEventListener("click", (e) => {
      if (e.target.closest('[data-act="login"]')) go("login");
    });
    return startFan(root);
  },
};

export default { login, signup };
