/* ==========================================================================
   에러 페이지 공통 스크립트 (error.html, error/404.html, error/5xx.html)
   - error.html?code=503&msg=메시지&from=lobby  → 코드/메시지를 주소로 전달
   - error/404.html 등은 <body data-code="404"> 로 기본 코드를 정한다
   ========================================================================== */
(function () {
  const INFO = {
    '400': { title: '잘못된 주문', desc: '요청 형식이 올바르지 않습니다.', rune: '✧' },
    '401': { title: '인증이 필요합니다', desc: '로그인이 만료되었습니다. 다시 로그인해주세요.', rune: '🔒' },
    '403': { title: '봉인된 구역', desc: '이 페이지에 접근할 권한이 없습니다.', rune: '⛨' },
    '404': { title: '사라진 차원', desc: '요청한 페이지를 찾을 수 없습니다. 주소를 다시 확인해주세요.', rune: '✦' },
    '500': { title: '마력 폭주', desc: '서버에서 오류가 발생했습니다. 잠시 후 다시 시도해주세요.', rune: '⚡' },
    '503': { title: '서버와 연결이 끊겼습니다', desc: 'Spring 서버가 실행 중인지, 네트워크가 연결되어 있는지 확인해주세요.', rune: '☄' },
    'offline': { title: '네트워크 연결 없음', desc: '인터넷(또는 같은 와이파이) 연결을 확인해주세요.', rune: '☁' },
  };

  const q = new URLSearchParams(location.search);
  let code = q.get('code') || document.body.getAttribute('data-code') || '404';
  if (code === '5xx') code = '500';
  const info = INFO[code] || (code.charAt(0) === '5' ? INFO['500'] : INFO['404']);
  const msg = q.get('msg');
  const from = q.get('from');

  document.title = code + ' · War of Wizards';
  document.getElementById('err-code').textContent = code === 'offline' ? '⚠' : code;
  document.getElementById('err-rune').textContent = info.rune;
  document.getElementById('err-title').textContent = info.title;
  document.getElementById('err-desc').textContent = msg || info.desc;

  const detail = document.getElementById('err-detail');
  detail.textContent = '요청 주소: ' + (from ? from + '.html' : location.pathname) + ' · ' + new Date().toLocaleString('ko-KR');

  // 다시 시도: 원래 페이지로 (from 이 있으면 그 페이지, 없으면 새로고침)
  document.getElementById('btn-retry').onclick = function () {
    if (from) location.href = '/' + from + '.html'; else location.reload();
  };
  document.getElementById('btn-back').onclick = function () {
    if (history.length > 1) history.back(); else location.href = '/';
  };
  // 401 이면 로그인 버튼을 강조
  if (code === '401') document.getElementById('btn-home').setAttribute('href', '/login.html');

  // 5초마다 서버 상태 확인 → 살아나면 알려준다 (503/offline 일 때)
  if (code === '503' || code === 'offline' || code.charAt(0) === '5') {
    const live = document.getElementById('err-live');
    live.classList.remove('hidden');
    const check = function () {
      fetch('/api/health', { cache: 'no-store' }).then(function (r) {
        if (!r.ok) throw new Error();
        live.innerHTML = '<span class="dot on"></span> 서버가 다시 응답합니다. <b>다시 시도</b>를 눌러주세요.';
      }).catch(function () {
        live.innerHTML = '<span class="dot"></span> 서버 응답을 기다리는 중… (5초마다 확인)';
      });
    };
    check();
    setInterval(check, 5000);
  }

  // 파티클 / 마법진 (common.js 없이도 동작하도록 간단히)
  const box = document.getElementById('err-particles');
  for (let i = 0; i < 12; i++) {
    const p = document.createElement('div');
    const size = Math.random() * 3 + 1;
    const color = Math.random() > 0.5 ? 'rgba(239,68,68,0.6)' : 'rgba(124,58,237,0.7)';
    p.className = 'particle';
    p.style.cssText = 'left:' + (Math.random() * 100) + '%;width:' + size + 'px;height:' + size + 'px;background:' + color +
      ';box-shadow:0 0 ' + (size * 3) + 'px ' + color + ';animation-duration:' + (Math.random() * 8 + 6) + 's;animation-delay:' + (Math.random() * 8) + 's';
    box.appendChild(p);
  }
})();
