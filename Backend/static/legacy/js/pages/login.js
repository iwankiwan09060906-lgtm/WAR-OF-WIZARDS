// 캔버스 1 · 로그인
WOW.page({ auth: false }, function () {
  WOW.decorate();
  WOW.bindPasswordToggles();

  const form = WOW.$('#login-form');
  const btn = WOW.$('#btn-login');
  const serverError = WOW.$('#server-error');

  function validate(email, pw) {
    let ok = true;
    WOW.fieldError('email', email.includes('@') ? '' : '올바른 이메일 주소를 입력해주세요.');
    WOW.fieldError('password', pw.length >= 6 ? '' : '비밀번호는 6자 이상이어야 합니다.');
    if (!email.includes('@') || pw.length < 6) ok = false;
    return ok;
  }

  async function login(email, pw) {
    btn.disabled = true;
    btn.innerHTML = '<span class="spinner"></span>주문 시전 중…';
    serverError.textContent = '';
    try {
      await WOW.api.login(email, pw);
      WOW.go('lobby');
    } catch (e) {
      serverError.textContent = e.message;
      btn.disabled = false;
      btn.textContent = '로그인';
    }
  }

  form.addEventListener('submit', function (e) {
    e.preventDefault();
    const email = WOW.$('#email').value.trim();
    const pw = WOW.$('#password').value;
    if (validate(email, pw)) login(email, pw);
  });

  WOW.$('#btn-demo').onclick = function () { login('demo@wow.dev', 'demo1234'); };

  WOW.$('#btn-reset').onclick = function () {
    if (!WOW.$('#email').value.includes('@')) { WOW.fieldError('email', '비밀번호를 재설정할 이메일을 먼저 입력해주세요.'); return; }
    WOW.toast('비밀번호 초기화는 관리자에게 문의해주세요.', 'error');
  };
});
