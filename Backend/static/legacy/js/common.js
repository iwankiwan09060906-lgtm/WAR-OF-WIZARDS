/* ==========================================================================
   모든 페이지 공통: 로그인 확인 / 페이지 이동 / 토스트 / 상태 저장 / UI 조각(아이콘, 바, 파티클)

   페이지 JS 의 기본 형태:
     WOW.page({ auth: true }, async function (ctx) {
       // ctx.auth    : 로그인 정보 (playerId, nickname, email, role ...)
       // ctx.profile : 내 프로필 (rating, wins, losses ...)
       // 여기에서 화면을 그리고 이벤트를 연결
     });
   ========================================================================== */
window.WOW = window.WOW || {};

(function (WOW) {
  // ── 페이지 목록 (DEV 메뉴 · 이동에 사용) ─────────────────────────────
  WOW.PAGES = [
    { no: 1,  id: 'login',     label: '로그인/가입', pub: true },
    { no: 2,  id: 'lobby',     label: '메인 로비' },
    { no: 3,  id: 'mypage',    label: '마이페이지' },
    { no: 4,  id: 'settings',  label: '환경설정' },
    { no: 5,  id: 'friends',   label: '친구' },
    { no: 6,  id: 'room',      label: '친선전 룸' },
    { no: 7,  id: 'mailbox',   label: '우편/공지' },
    { no: 8,  id: 'ranking',   label: '랭킹' },
    { no: 9,  id: 'spellbook', label: '마법서' },
    { no: 10, id: 'queue',     label: '매칭 대기' },
    { no: 11, id: 'countdown', label: '카운트다운' },
    { no: 12, id: 'battle',    label: '전투 HUD' },
    { no: 13, id: 'result',    label: '결과' },
    { no: 14, id: 'admin',     label: '관리자' },
  ];

  /** 페이지 이동: WOW.go('lobby') → lobby.html */
  WOW.go = function (id, query) {
    location.href = id + '.html' + (query ? '?' + new URLSearchParams(query).toString() : '');
  };
  WOW.query = function (name) { return new URLSearchParams(location.search).get(name); };
  WOW.currentPage = function () {
    const f = location.pathname.split('/').pop() || 'index.html';
    return f.replace('.html', '');
  };

  // ── 포맷 ─────────────────────────────────────────────────────────────
  WOW.esc = function (v) {
    return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  };
  WOW.fmtTime = function (sec) {
    const s = Math.max(0, Math.floor(sec));
    return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  };
  WOW.winRate = function (w, l) { const n = w + l; return n === 0 ? 0 : Math.round((w / n) * 100); };
  WOW.tierOf = function (rating) {
    if (rating >= 1500) return { name: '대마법사', color: '#fbbf24' };
    if (rating >= 1300) return { name: '마법사', color: '#c084fc' };
    if (rating >= 1100) return { name: '숙련 마법사', color: '#22d3ee' };
    if (rating >= 900)  return { name: '견습 마법사', color: '#a78bfa' };
    return { name: '입문자', color: '#94a3b8' };
  };
  WOW.timeAgo = function (iso) {
    const diff = (Date.now() - new Date(iso).getTime()) / 1000;
    if (diff < 60) return '방금 전';
    if (diff < 3600) return Math.floor(diff / 60) + '분 전';
    if (diff < 86400) return Math.floor(diff / 3600) + '시간 전';
    return Math.floor(diff / 86400) + '일 전';
  };
  WOW.MODE_LABEL = { ranked: '랭킹전', friendly: '친선전', bot: '봇 연습전' };
  WOW.ROOM_CODE_RE = /^[A-Z0-9]{6}$/;

  // ── 상태 저장 (페이지가 바뀌어도 유지) ───────────────────────────────
  //   sessionStorage: 탭을 닫으면 사라지는 값 (진행 중인 매치, 룸, 마지막 결과)
  //   localStorage  : 계속 남는 값 (환경설정)
  function readJson(storage, key, fallback) {
    try { const raw = storage.getItem(key); return raw ? JSON.parse(raw) : fallback; } catch (e) { return fallback; }
  }
  function writeJson(storage, key, value) {
    try {
      if (value == null) storage.removeItem(key); else storage.setItem(key, JSON.stringify(value));
    } catch (e) { /* 저장 실패 무시 */ }
  }
  WOW.state = {
    /** 시작할 매치: { mode: 'ranked'|'friendly'|'bot', opponent: { name, rating, isBot }, roomCode? } */
    getMatch: function () { return readJson(sessionStorage, 'wow.match', null); },
    setMatch: function (m) { writeJson(sessionStorage, 'wow.match', m); },
    /** 끝난 매치 결과 (결과 화면에서 사용) */
    getLastMatch: function () { return readJson(sessionStorage, 'wow.lastMatch', null); },
    setLastMatch: function (m) { writeJson(sessionStorage, 'wow.lastMatch', m); },
    /** 현재 친선전 룸: { code, role: 'host'|'guest' } */
    getRoom: function () { return readJson(sessionStorage, 'wow.room', null); },
    setRoom: function (r) { writeJson(sessionStorage, 'wow.room', r); },
    clearAll: function () { ['wow.match', 'wow.lastMatch', 'wow.room'].forEach(function (k) { try { sessionStorage.removeItem(k); } catch (e) { /* */ } }); },
  };

  /** 매치 시작 (로비/룸/결과 화면에서 호출) */
  WOW.beginMatch = function (mode, opponent, extra) {
    WOW.state.setMatch(Object.assign({ mode: mode, opponent: opponent, token: newToken() }, extra || {}));
    WOW.state.setLastMatch(null);
  };
  function newToken() {
    if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
    return 'm-' + Date.now() + '-' + Math.random().toString(36).slice(2, 10);
  }

  // ── 환경설정 ─────────────────────────────────────────────────────────
  const SETTINGS_KEY = 'wow.settings.v1';
  WOW.DEFAULT_SETTINGS = {
    camSensitivity: 65, comfortVignette: true, snapTurning: false,
    sfx: 80, bgm: 45, spatialAudio: true,
    gestureSensitivity: 70, holdFrames: 15, haptics: true,
    subtitles: false, uiLarge: true, colorblind: false,
  };
  WOW.settings = {
    get: function () { return Object.assign({}, WOW.DEFAULT_SETTINGS, readJson(localStorage, SETTINGS_KEY, {})); },
    update: function (patch) { const n = Object.assign(WOW.settings.get(), patch); writeJson(localStorage, SETTINGS_KEY, n); applySettings(n); return n; },
    reset: function () { writeJson(localStorage, SETTINGS_KEY, WOW.DEFAULT_SETTINGS); applySettings(WOW.DEFAULT_SETTINGS); return WOW.settings.get(); },
  };
  function applySettings(s) { document.documentElement.classList.toggle('ui-large', !!s.uiLarge); }
  applySettings(WOW.settings.get());

  // ── 토스트 ───────────────────────────────────────────────────────────
  WOW.toast = function (msg, tone) {
    let box = document.getElementById('toasts');
    if (!box) { box = document.createElement('div'); box.id = 'toasts'; box.setAttribute('aria-live', 'polite'); document.body.appendChild(box); }
    const el = document.createElement('button');
    el.className = 'toast ' + (tone || 'info');
    el.textContent = msg;
    el.onclick = function () { el.remove(); };
    box.appendChild(el);
    while (box.children.length > 4) box.firstChild.remove();
    setTimeout(function () { el.remove(); }, 3600);
  };
  /** 버튼 연타 방지 + 오류 토스트. 성공 메시지는 okMsg */
  WOW.run = async function (btn, fn, okMsg) {
    if (btn && btn.disabled) return;
    if (btn) btn.disabled = true;
    try { const r = await fn(); if (okMsg) WOW.toast(okMsg, 'success'); return r; }
    catch (e) { WOW.toast(e.message, 'error'); }
    finally { if (btn) btn.disabled = false; }
  };

  // ── 세션 / 페이지 시작 ───────────────────────────────────────────────
  WOW.ctx = { auth: null, profile: null, deck: null, online: [] };

  WOW.loadProfile = async function () { WOW.ctx.profile = await WOW.api.myProfile(); return WOW.ctx.profile; };
  WOW.loadDeck = async function () { WOW.ctx.deck = await WOW.api.loadDeck(); return WOW.ctx.deck; };
  WOW.isAdmin = function () { return !!(WOW.ctx.auth && WOW.ctx.auth.role === 'ROLE_ADMIN'); };

  /**
   * 페이지 시작점.
   * @param {object} opt  { auth: true(로그인 필요) | false(로그인 화면), deck: true(덱도 불러오기) }
   * @param {function} init  async function(ctx) — 화면 그리기
   */
  WOW.page = function (opt, init) {
    document.addEventListener('DOMContentLoaded', async function () {
      WOW.loading.show('마법사 정보를 확인하는 중…');
      let auth = null;
      try {
        auth = await WOW.api.me();
      } catch (e) {
        // 서버가 꺼져 있으면 로그인 화면으로 보내지 않고 에러 페이지로
        if (e.status === 0 || e.status >= 500) { WOW.goError(e.status === 0 ? 503 : e.status, e.message); return; }
        WOW.toast(e.message, 'error');
      }
      WOW.ctx.auth = auth;

      if (opt.auth && !auth) { WOW.go('login'); return; }
      if (opt.auth === false && auth) { WOW.go('lobby'); return; }

      try {
        if (auth) {
          WOW.loading.message('마법서를 펼치는 중…');
          const jobs = [WOW.loadProfile()];
          if (opt.deck) jobs.push(WOW.loadDeck());
          await Promise.all(jobs);
          startPresence();
          await WOW.live.loadConfig();
          WOW.live.connect();
        }
        renderDevNav();
        await init(WOW.ctx);
      } catch (e) {
        console.error(e);
        WOW.showError({ title: '화면을 불러오지 못했습니다', message: e.message || String(e), retry: true, status: e.status });
      } finally {
        WOW.loading.hide();
      }
    });
  };

  // ── 로딩 화면 ─────────────────────────────────────────────────────────
  //   WOW.loading.show('메시지') / WOW.loading.message('바뀐 메시지') / WOW.loading.hide()
  //   오래 걸리는 작업 예:  WOW.loading.show('저장 중…'); await ...; WOW.loading.hide();
  const LOADING_TIPS = [
    '룬을 그릴 때는 손을 카메라 앞에 두세요.',
    '고위력 스킬은 게임당 3회만 쓸 수 있습니다.',
    '적 타워를 부수면 공격력이 영구 +20% 강해집니다.',
    '덱은 19종 중 8장, 고위력은 최대 2장입니다.',
    '친구에게 룸 코드를 알려주면 친선전을 할 수 있어요.',
  ];
  WOW.loading = {
    show: function (msg) {
      let el = document.getElementById('loading');
      if (!el) {
        el = document.createElement('div');
        el.id = 'loading';
        el.className = 'loading-screen';
        el.setAttribute('role', 'status');
        el.innerHTML =
          '<div class="loading-magic">' + WOW.magicCircle(200) + '<div class="loading-orb"></div></div>' +
          '<div class="loading-title">WAR OF WIZARDS</div>' +
          '<div class="loading-bar"><i></i></div>' +
          '<div class="loading-msg"></div>' +
          '<div class="loading-tip">💡 ' + LOADING_TIPS[Math.floor(Math.random() * LOADING_TIPS.length)] + '</div>';
        (document.body || document.documentElement).appendChild(el);
      }
      el.classList.remove('fade-out');
      WOW.loading.message(msg || '마력을 모으는 중…');
    },
    message: function (msg) {
      const m = document.querySelector('#loading .loading-msg');
      if (m) m.textContent = msg;
    },
    hide: function () {
      const el = document.getElementById('loading');
      if (!el) return;
      el.classList.add('fade-out');
      setTimeout(function () { if (el.classList.contains('fade-out')) el.remove(); }, 350);
    },
  };

  // ── 에러 ─────────────────────────────────────────────────────────────
  /** 에러 페이지로 이동: WOW.goError(503, '메시지') */
  WOW.goError = function (code, msg) {
    location.href = 'error.html?' + new URLSearchParams({ code: String(code), msg: msg || '', from: WOW.currentPage() }).toString();
  };

  /**
   * 현재 화면 위에 에러 창 띄우기 (페이지는 유지)
   * @param {object} o { title, message, retry(true면 '다시 시도'=새로고침), status }
   */
  WOW.showError = function (o) {
    WOW.loading.hide();
    let el = document.getElementById('error-overlay');
    if (el) el.remove();
    if (o.status === 401) { WOW.go('login'); return; }
    el = document.createElement('div');
    el.id = 'error-overlay';
    el.className = 'error-overlay';
    el.innerHTML =
      '<div class="card corners error-card">' +
      '<div class="error-rune">⚠</div>' +
      '<h2 class="display" style="font-size:1.5rem;color:var(--purple-100)">' + WOW.esc(o.title || '문제가 발생했습니다') + '</h2>' +
      '<p class="dim" style="line-height:1.6">' + WOW.esc(o.message || '') + '</p>' +
      '<div class="row wrap" style="justify-content:center">' +
      (o.retry ? '<button class="btn btn-primary btn-sm" data-act="retry">다시 시도</button>' : '') +
      '<button class="btn btn-ghost btn-sm" data-act="close">닫기</button>' +
      '<a class="btn btn-secondary btn-sm" href="lobby.html">로비로</a></div></div>';
    el.onclick = function (e) {
      const act = e.target.getAttribute('data-act');
      if (act === 'retry') location.reload();
      if (act === 'close') el.remove();
    };
    document.body.appendChild(el);
  };

  // 예상하지 못한 스크립트 오류도 콘솔 대신 토스트로 알려준다
  window.addEventListener('unhandledrejection', function (e) {
    const err = e.reason || {};
    if (err.status === 0) WOW.toast(err.message, 'error');
  });
  window.addEventListener('offline', function () { WOW.toast('네트워크 연결이 끊겼습니다.', 'error'); });
  window.addEventListener('online', function () { WOW.toast('네트워크에 다시 연결되었습니다.', 'success'); });

  // 접속 상태: 20초마다 하트비트 → 응답으로 접속 중인 친구 ID 목록
  const presenceListeners = [];
  WOW.onPresence = function (cb) { presenceListeners.push(cb); cb(WOW.ctx.online); };
  function startPresence() {
    const tick = async function () {
      try {
        const r = await WOW.api.heartbeat();
        WOW.ctx.online = (r.onlineFriendIds || []).map(String);
        presenceListeners.forEach(function (cb) { cb(WOW.ctx.online); });
      } catch (e) { /* 다음 주기에 재시도 */ }
    };
    tick();
    setInterval(tick, 20000);
  }

  // ── DB → 웹 실시간 데이터 (Unity 의 LiveEventStream 과 같은 채널) ─────────
  //   WOW.config['motd']            : 서버 game_configs 값 (문자열)
  //   window.addEventListener('wow:live', e => console.log(e.detail))   : 페이지에서 이벤트 받기
  WOW.config = {};
  WOW.live = {
    /** game_configs 를 받아 WOW.config 와 WOW.RULES(전투 규칙)에 반영 */
    loadConfig: async function () {
      try { applyConfigs(await WOW.api.liveConfig()); } catch (e) { /* 기본 규칙으로 진행 */ }
    },
    connect: function () {
      if (WOW.live.source || !window.EventSource || !WOW.api.hasToken()) return;
      const es = new EventSource(WOW.api.liveEventsUrl());
      es.onmessage = function (m) {
        let e;
        try { e = JSON.parse(m.data); } catch (err) { return; }
        handleLiveEvent(e);
        window.dispatchEvent(new CustomEvent('wow:live', { detail: e }));
      };
      WOW.live.source = es;   // 끊기면 브라우저가 자동으로 다시 연결한다
      window.addEventListener('pagehide', function () { es.close(); });
    },
  };

  function applyConfigs(list) {
    (list || []).forEach(function (c) {
      WOW.config[c.key] = c.value;
      if (c.type !== 'NUMBER') return;
      // 'cooldown.normal' → WOW.RULES.cooldown.normal
      const path = c.key.split('.');
      let obj = WOW.RULES;
      for (let i = 0; i < path.length - 1; i++) { obj = obj && obj[path[i]]; }
      const last = path[path.length - 1];
      if (obj && typeof obj[last] === 'number') obj[last] = Number(c.value);
    });
  }

  function handleLiveEvent(e) {
    const text = e.title + (e.message ? ' — ' + e.message : '');
    switch (e.type) {
      case 'config':
        try { applyConfigs(JSON.parse(e.payload)); } catch (err) { /* 무시 */ }
        WOW.toast('게임 설정이 변경되었습니다: ' + e.message, 'info');
        break;
      case 'mail': case 'notice': WOW.toast('📬 ' + text, 'info'); break;
      case 'invite': WOW.toast('✉ ' + text + ' (우편함에서 입장)', 'success'); break;
      case 'friend': WOW.toast('👥 ' + e.message, 'info'); break;
      case 'hello': break;
      default: WOW.toast('📢 ' + text, 'info');
    }
  }

  WOW.logout = async function () {
    if (WOW.live.source) WOW.live.source.close();
    await WOW.api.logout();
    WOW.state.clearAll();
    WOW.go('login');
  };

  // DEV 메뉴 (좌상단 ☰): 13종 캔버스로 바로 이동. 필요 없으면 페이지에 data-no-dev 속성 추가
  function renderDevNav() {
    if (document.body.hasAttribute('data-no-dev')) return;
    const cur = WOW.currentPage() === 'signup' ? 'login' : WOW.currentPage();
    const nav = document.createElement('div');
    nav.id = 'dev-nav';
    const btn = document.createElement('button');
    const links = document.createElement('div');
    links.className = 'links hidden';
    WOW.PAGES.forEach(function (p) {
      if (p.id === 'admin' && !WOW.isAdmin()) return;
      const a = document.createElement('a');
      a.href = p.id + '.html';
      a.textContent = p.no + '. ' + p.label;
      if (p.id === cur) a.className = 'current';
      if (!WOW.ctx.auth && !p.pub) a.className += ' disabled';
      links.appendChild(a);
    });
    btn.textContent = '☰ DEV · Spring';
    btn.onclick = function () { links.classList.toggle('hidden'); btn.textContent = (links.classList.contains('hidden') ? '☰' : '✕') + ' DEV · Spring'; };
    nav.appendChild(btn); nav.appendChild(links);
    document.body.appendChild(nav);
  }

  // ── UI 조각 ──────────────────────────────────────────────────────────
  /** 떠오르는 파티클. box 는 <div class="particles" data-particles="14"></div> 같은 빈 요소 */
  WOW.particles = function (box, count) {
    box.classList.add('particles');
    for (let i = 0; i < (count || 14); i++) {
      const p = document.createElement('div');
      const size = Math.random() * 3 + 1;
      const color = Math.random() > 0.5 ? 'rgba(124,58,237,0.7)' : 'rgba(6,182,212,0.7)';
      p.className = 'particle';
      p.style.cssText = 'left:' + (Math.random() * 100) + '%;width:' + size + 'px;height:' + size + 'px;background:' + color +
        ';box-shadow:0 0 ' + (size * 3) + 'px ' + color + ';animation-duration:' + (Math.random() * 8 + 6) + 's;animation-delay:' + (Math.random() * 10) + 's';
      box.appendChild(p);
    }
  };

  /** 회전하는 마법진 SVG 문자열 */
  WOW.magicCircle = function (size) {
    const r1 = size * 0.46, r2 = size * 0.38, r3 = size * 0.28, c = size / 2;
    const pts = [];
    for (let i = 0; i < 5; i++) {
      const a = (i * 4 * Math.PI) / 5 - Math.PI / 2;
      pts.push((c + r2 * 0.92 * Math.cos(a)) + ',' + (c + r2 * 0.92 * Math.sin(a)));
    }
    const o = 'style="transform-origin:' + c + 'px ' + c + 'px"';
    return '<svg width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size + '">' +
      '<circle cx="' + c + '" cy="' + c + '" r="' + r1 + '" fill="none" stroke="rgba(124,58,237,0.5)" stroke-width="1.5" class="spin-slow" ' + o + '/>' +
      '<circle cx="' + c + '" cy="' + c + '" r="' + r1 + '" fill="none" stroke="rgba(124,58,237,0.25)" stroke-width="0.5" stroke-dasharray="3 18" class="spin-reverse" ' + o + '/>' +
      '<circle cx="' + c + '" cy="' + c + '" r="' + r2 + '" fill="none" stroke="rgba(6,182,212,0.45)" stroke-width="1" class="spin-reverse" ' + o + '/>' +
      '<circle cx="' + c + '" cy="' + c + '" r="' + r2 + '" fill="none" stroke="rgba(6,182,212,0.15)" stroke-width="0.5" stroke-dasharray="6 12" class="spin-slow" ' + o + '/>' +
      '<circle cx="' + c + '" cy="' + c + '" r="' + r3 + '" fill="none" stroke="rgba(167,139,250,0.4)" stroke-width="1" class="spin-slow" style="transform-origin:' + c + 'px ' + c + 'px;animation-duration:8s"/>' +
      '<polygon points="' + pts.join(' ') + '" fill="none" stroke="rgba(124,58,237,0.3)" stroke-width="0.75"/>' +
      '<circle cx="' + c + '" cy="' + c + '" r="' + (size * 0.04) + '" fill="rgba(124,58,237,0.15)" stroke="rgba(167,139,250,0.6)" class="pulse-glow"/>' +
      '<line x1="' + c + '" y1="' + (c - r1) + '" x2="' + c + '" y2="' + (c + r1) + '" stroke="rgba(124,58,237,0.1)" stroke-width="0.5"/>' +
      '<line x1="' + (c - r1) + '" y1="' + c + '" x2="' + (c + r1) + '" y2="' + c + '" stroke="rgba(124,58,237,0.1)" stroke-width="0.5"/>' +
      '</svg>';
  };

  /** HTML 안의 <div data-magic="700" data-opacity="0.2"></div> 와 <div data-particles="14"></div> 를 채운다 */
  WOW.decorate = function (root) {
    (root || document).querySelectorAll('[data-magic]').forEach(function (el) {
      el.innerHTML = WOW.magicCircle(Number(el.getAttribute('data-magic')));
    });
    (root || document).querySelectorAll('[data-particles]').forEach(function (el) {
      WOW.particles(el, Number(el.getAttribute('data-particles')));
    });
  };

  // 스킬 종류별 기호 (실제 룬 아트로 교체할 자리)
  function glyph(kind, color, s) {
    const h = '<svg width="' + s + '" height="' + s + '" viewBox="0 0 48 48" fill="none">';
    switch (kind) {
      case 'projectile': return h + '<circle cx="32" cy="24" r="8" fill="' + color + '"/><path d="M6 24h16M9 16l12 5M9 32l12-5" stroke="' + color + '" stroke-width="3" stroke-linecap="round" opacity="0.8"/></svg>';
      case 'area': return h + '<circle cx="24" cy="24" r="4" fill="' + color + '"/><circle cx="24" cy="24" r="11" stroke="' + color + '" stroke-width="2.5"/><circle cx="24" cy="24" r="18" stroke="' + color + '" stroke-width="2" stroke-dasharray="4 4" opacity="0.7"/></svg>';
      case 'beam': return h + '<rect x="20" y="3" width="8" height="42" rx="4" fill="' + color + '"/><rect x="14" y="3" width="20" height="42" rx="8" stroke="' + color + '" stroke-width="2" opacity="0.5"/></svg>';
      case 'buff': return h + '<path d="M24 4 40 11v12c0 10-16 19-16 19S8 33 8 23V11z" fill="' + color + '" opacity="0.9"/><path d="M17 23l5 5 9-10" stroke="#0b0b1a" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/></svg>';
      case 'summon': return h + '<circle cx="24" cy="15" r="8" fill="' + color + '"/><path d="M9 42c0-11 6-16 15-16s15 5 15 16z" fill="' + color + '" opacity="0.75"/></svg>';
      case 'install': return h + '<path d="M10 42V22l14-14 14 14v20z" fill="' + color + '" opacity="0.85"/><rect x="19" y="28" width="10" height="14" fill="#0b0b1a" opacity="0.6"/></svg>';
      default: return h + '<circle cx="24" cy="24" r="17" stroke="' + color + '" stroke-width="3"/><ellipse cx="24" cy="24" rx="8" ry="17" stroke="' + color + '" stroke-width="2" opacity="0.8"/><path d="M7 24h34" stroke="' + color + '" stroke-width="2" opacity="0.8"/></svg>';
    }
  }

  /**
   * 육각형 스킬 아이콘 HTML
   * @param {string} id  스킬 코드 (S01 …)
   * @param {number} size
   * @param {object} [o] { active: true, dim: false }
   */
  WOW.skillIcon = function (id, size, o) {
    const k = WOW.SKILL_MAP[id];
    if (!k) return '';
    size = size || 64; o = o || {};
    const lit = o.active !== false && !o.dim;
    const high = k.tier === 'high';
    const stroke = high ? '#fbbf24' : lit ? k.color + '99' : 'rgba(124,58,237,0.3)';
    return '<div class="skill-icon" title="' + WOW.esc(k.name) + '" style="width:' + size + 'px;height:' + size + 'px;opacity:' + (o.dim ? 0.4 : 1) + '">' +
      '<div class="hex" style="background:' + (lit ? k.color + '22' : 'rgba(255,255,255,0.04)') + ';box-shadow:' + (lit ? '0 0 ' + (size * 0.4) + 'px ' + k.glow : 'none') + '">' +
      glyph(k.kind, lit ? k.color : '#6b6a8a', size * 0.52) + '</div>' +
      '<svg class="frame" width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size + '" fill="none"><polygon points="' +
      (size / 2) + ',2 ' + (size - 4) + ',' + (size * 0.27) + ' ' + (size - 4) + ',' + (size * 0.73) + ' ' + (size / 2) + ',' + (size - 2) + ' 4,' + (size * 0.73) + ' 4,' + (size * 0.27) +
      '" stroke="' + stroke + '" stroke-width="' + (high ? 2.5 : 1.5) + '"/></svg>' +
      (high ? '<span class="star" aria-hidden="true">★</span>' : '') + '</div>';
  };

  const BAR_COLORS = {
    purple: ['139,92,246'], cyan: ['6,182,212'], orange: ['249,115,22'], red: ['239,68,68'],
  };
  /** 에너지 바 HTML: WOW.bar(현재값, 최대값, 'cyan', { label, showValue, height }) */
  WOW.bar = function (value, max, color, o) {
    o = o || {};
    const rgb = (BAR_COLORS[color] || BAR_COLORS.purple)[0];
    const pct = Math.min(100, Math.max(0, (value / (max || 100)) * 100));
    const h = o.height || 12;
    const head = (o.label || o.showValue)
      ? '<div class="bar-head"><span>' + WOW.esc(o.label || '') + '</span>' + (o.showValue ? '<span>' + value + '/' + max + '</span>' : '') + '</div>'
      : '';
    return '<div class="bar">' + head +
      '<div class="bar-track" style="height:' + h + 'px;background:rgba(' + rgb + ',0.1);border:1px solid rgba(' + rgb + ',0.3)">' +
      '<div class="bar-fill" style="width:' + pct + '%;background:linear-gradient(90deg,rgba(' + rgb + ',0.7),rgba(' + rgb + ',1));box-shadow:0 0 ' + h + 'px rgba(' + rgb + ',0.6)"></div>' +
      '</div></div>';
  };

  /** 자주 쓰는 아이콘 SVG */
  WOW.icon = {
    email: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="M2 7l10 7 10-7"/></svg>',
    lock: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V7a4 4 0 018 0v4"/><circle cx="12" cy="16" r="1.5" fill="currentColor" stroke="none"/></svg>',
    user: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><path d="M20 21v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
    shield: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9 12l2 2 4-4"/></svg>',
    eye: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>',
  };

  /** 비밀번호 보기 토글: <button data-toggle-pw="inputId"> */
  WOW.bindPasswordToggles = function () {
    document.querySelectorAll('[data-toggle-pw]').forEach(function (b) {
      b.innerHTML = WOW.icon.eye;
      b.onclick = function () {
        const input = document.getElementById(b.getAttribute('data-toggle-pw'));
        input.type = input.type === 'password' ? 'text' : 'password';
      };
    });
    document.querySelectorAll('[data-icon]').forEach(function (el) { el.innerHTML = WOW.icon[el.getAttribute('data-icon')] || ''; });
  };

  /** 입력칸 오류 표시: WOW.fieldError('email', '메시지') / 빈 문자열이면 해제 */
  WOW.fieldError = function (name, msg) {
    const box = document.querySelector('[data-field="' + name + '"] .input-box');
    const err = document.querySelector('[data-field="' + name + '"] .field-error');
    if (box) box.classList.toggle('invalid', !!msg);
    if (err) err.textContent = msg || '';
  };

  /** 간단한 DOM 도우미 */
  WOW.$ = function (sel, root) { return (root || document).querySelector(sel); };
  WOW.$$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  // 페이지가 그려지자마자 로딩 화면을 덮어 빈 화면이 깜빡이지 않게 한다 (WOW.page 가 끝나면 사라짐)
  if (document.body && !document.body.hasAttribute('data-no-loading')) WOW.loading.show();
})(window.WOW);
