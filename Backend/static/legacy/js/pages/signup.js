// 캔버스 1 · 회원가입
WOW.page({ auth: false }, function () {
  WOW.decorate();
  WOW.bindPasswordToggles();

  const $ = WOW.$;
  const btn = $('#btn-signup');
  const NICK_RE = /^[A-Za-z0-9가-힣_]{2,12}$/;

  // 비밀번호 강도 표시
  $('#password').addEventListener('input', function () {
    const pw = this.value;
    const box = $('#strength');
    box.classList.toggle('hidden', !pw);
    let score = 0;
    if (pw.length >= 8) score++;
    if (/[A-Z]/.test(pw)) score++;
    if (/[0-9]/.test(pw)) score++;
    if (/[^A-Za-z0-9]/.test(pw)) score++;
    const colors = ['#ef4444', '#ef4444', '#f97316', '#06b6d4', '#22c55e'];
    const labels = ['약함', '약함', '보통', '강함', '매우 강함'];
    WOW.$$('.strength-bars i', box).forEach(function (bar, i) {
      bar.style.background = i < score ? colors[score] : 'rgba(124,58,237,0.15)';
    });
    const label = $('span', box);
    label.textContent = '비밀번호 강도: ' + labels[score];
    label.style.color = colors[score];
  });

  // 닉네임 중복 확인 (입력을 멈추고 0.5초 뒤 서버에 확인)
  let nickTimer = 0;
  $('#nickname').addEventListener('input', function () {
    clearTimeout(nickTimer);
    const nick = this.value.trim();
    const hint = $('#nick-hint');
    hint.textContent = '2–12자, 한글·영문·숫자·언더바(_) 사용 가능';
    hint.style.color = '';
    if (!NICK_RE.test(nick)) return;
    nickTimer = setTimeout(async function () {
      try {
        const ok = await WOW.api.nicknameAvailable(nick);
        hint.textContent = ok ? '사용할 수 있는 닉네임입니다.' : '이미 사용 중인 닉네임입니다.';
        hint.style.color = ok ? 'var(--cyan-300)' : 'var(--red-300)';
      } catch (e) { /* 무시 */ }
    }, 500);
  });

  function validate(v) {
    const e = {};
    if (!NICK_RE.test(v.nickname)) e.nickname = '닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.';
    if (!v.email.includes('@')) e.email = '올바른 이메일을 입력해주세요.';
    if (v.password.length < 6) e.password = '비밀번호는 6자 이상이어야 합니다.';
    if (v.confirm !== v.password) e.confirm = '비밀번호가 일치하지 않습니다.';
    if (!v.terms) e.terms = '필수 약관에 동의해주세요.';
    ['nickname', 'email', 'password', 'confirm', 'terms'].forEach(function (k) { WOW.fieldError(k, e[k] || ''); });
    return Object.keys(e).length === 0;
  }

  $('#signup-form').addEventListener('submit', async function (ev) {
    ev.preventDefault();
    if (btn.disabled) return;
    const v = {
      nickname: $('#nickname').value.trim(),
      email: $('#email').value.trim(),
      password: $('#password').value,
      confirm: $('#confirm').value,
      terms: $('#agree-terms').checked,
    };
    if (!validate(v)) return;

    btn.disabled = true;
    btn.innerHTML = '<span class="spinner"></span>계정 생성 중…';
    $('#server-error').textContent = '';
    try {
      if (!(await WOW.api.nicknameAvailable(v.nickname))) {
        WOW.fieldError('nickname', '이미 사용 중인 닉네임입니다.');
        return;
      }
      await WOW.api.register(v.email, v.password, v.nickname);
      sessionStorage.setItem('wow.welcome', v.nickname);
      WOW.go('lobby');
    } catch (e) {
      $('#server-error').textContent = e.message;
    } finally {
      btn.disabled = false;
      btn.textContent = '마법사 등록하기';
    }
  });
});
