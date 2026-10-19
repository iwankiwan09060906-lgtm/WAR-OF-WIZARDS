/* ==========================================================================
   Spring 백엔드 API 클라이언트  (모든 서버 호출은 이 파일을 거칩니다)
   - 같은 서버(:8080)에서 HTML 을 서비스하므로 주소는 '/api' 그대로 사용
   - 다른 서버를 쓰려면 페이지에서 api.js 보다 먼저 window.WOW_API_BASE = 'http://192.168.0.10:8080/api' 지정
   - 토큰 키 'wow.spring.token' 은 Unity WebGL(/game/) 도 같이 읽어 자동 로그인합니다.

   ▶ 새 기능 추가 방법
     1) 서버에 Controller 메서드 추가 (예: GET /api/shop/items)
     2) 아래 WOW.api 에 한 줄 추가:  shopItems: function () { return request('/shop/items'); },
     3) 페이지 JS 에서  const items = await WOW.api.shopItems();
   ========================================================================== */
window.WOW = window.WOW || {};

(function (WOW) {
  const BASE_URL = (window.WOW_API_BASE || '/api').replace(/\/$/, '');
  const TOKEN_KEY = 'wow.spring.token';

  function getToken() { try { return localStorage.getItem(TOKEN_KEY); } catch (e) { return null; } }
  function setToken(t) { try { localStorage.setItem(TOKEN_KEY, t); } catch (e) { /* 무시 */ } }
  function clearToken() { try { localStorage.removeItem(TOKEN_KEY); } catch (e) { /* 무시 */ } }

  /**
   * 공통 요청 함수. 실패하면 서버가 보낸 한글 message 로 Error 를 던진다.
   * @param {string} path   '/players/me' 처럼 /api 뒤 경로
   * @param {object} [opt]  { method, body(객체면 JSON 으로 변환) }
   */
  async function request(path, opt) {
    opt = opt || {};
    const headers = {};
    const token = getToken();
    if (token) headers['Authorization'] = 'Bearer ' + token;
    let body = opt.body;
    if (body !== undefined && typeof body !== 'string') body = JSON.stringify(body);
    if (body !== undefined) headers['Content-Type'] = 'application/json';

    let res;
    try {
      res = await fetch(BASE_URL + path, { method: opt.method || 'GET', headers: headers, body: body });
    } catch (e) {
      const netErr = new Error('서버에 연결할 수 없습니다. Spring 서버가 실행 중인지 확인해주세요.');
      netErr.status = 0; // 0 = 네트워크 오류 (서버 꺼짐 / 인터넷 끊김)
      throw netErr;
    }
    if (!res.ok) {
      let message = '요청 실패 (' + res.status + ')';
      try { const j = await res.json(); if (j && j.message) message = j.message; } catch (e) { /* JSON 아님 */ }
      if (res.status === 401 && path !== '/auth/login') clearToken();
      const err = new Error(message);
      err.status = res.status;
      throw err;
    }
    if (res.status === 204) return null;
    const text = await res.text();
    return text ? JSON.parse(text) : null;
  }

  const enc = encodeURIComponent;

  WOW.api = {
    request: request,
    getToken: getToken,
    hasToken: function () { return !!getToken(); },

    // ── 인증 ───────────────────────────────────────────────
    /** 저장된 토큰으로 세션 복구. 실패하면 null */
    me: async function () {
      if (!getToken()) return null;
      try {
        const a = await request('/auth/me');
        setToken(a.accessToken); // 서버가 만료 시간을 연장한 새 토큰을 준다
        return a;
      } catch (e) {
        if (e.status === 401 || e.status === 403 || e.status === 404) { clearToken(); return null; }
        throw e;
      }
    },
    login: async function (email, password) {
      const a = await request('/auth/login', { method: 'POST', body: { email: email, password: password } });
      setToken(a.accessToken);
      return a;
    },
    register: async function (email, password, nickname) {
      const a = await request('/auth/register', { method: 'POST', body: { email: email, password: password, nickname: nickname } });
      setToken(a.accessToken);
      return a;
    },
    logout: async function () {
      try { await request('/auth/logout', { method: 'POST' }); } catch (e) { /* 이미 만료 */ }
      clearToken();
    },
    changePassword: function (currentPassword, newPassword) {
      return request('/auth/password', { method: 'PUT', body: { currentPassword: currentPassword, newPassword: newPassword } });
    },
    nicknameAvailable: async function (nickname) {
      const r = await request('/auth/nickname-available?nickname=' + enc(nickname));
      return r.available;
    },

    // ── 프로필 / 랭킹 ─────────────────────────────────────
    myProfile: function () { return request('/players/me'); },
    profile: function (playerId) { return request('/players/' + playerId); },
    updateNickname: function (nickname) { return request('/players/me', { method: 'PUT', body: { nickname: nickname } }); },
    ranking: function (limit) { return request('/players/ranking?limit=' + (limit || 100)); },

    // ── 스킬 / 덱 ─────────────────────────────────────────
    skills: function () { return request('/skills'); },
    loadDeck: async function () { const r = await request('/decks/me'); return (r && r.skills) || []; },
    saveDeck: function (skills) { return request('/decks/me', { method: 'PUT', body: { skills: skills } }); },

    // ── 전적 ─────────────────────────────────────────────
    matches: function (playerId, limit) { return request('/matches/player/' + playerId + '?limit=' + (limit || 30)); },
    recordMatch: function (m) {
      return request('/matches', {
        method: 'POST',
        body: {
          mode: m.mode, result: m.result, opponent: m.opponent,
          durationSec: Math.round(m.durationSec), casts: m.casts,
          damageDealt: Math.round(m.damageDealt || 0), damageTaken: Math.round(m.damageTaken || 0),
          matchToken: m.matchToken,
        },
      });
    },

    // ── 친구 / 접속 상태 ──────────────────────────────────
    friends: function () { return request('/friends'); },
    requestFriend: function (nickname) { return request('/friends/requests', { method: 'POST', body: { nickname: nickname.trim() } }); },
    respondFriend: function (id, accept) { return request('/friends/' + id + '/' + (accept ? 'accept' : 'reject'), { method: 'POST' }); },
    removeFriend: function (id) { return request('/friends/' + id, { method: 'DELETE' }); },
    heartbeat: function () { return request('/presence/heartbeat', { method: 'POST' }); },

    // ── 우편 / 공지 / 초대 ────────────────────────────────
    mails: function () { return request('/mails'); },
    unreadCount: async function () { const r = await request('/mails/unread-count'); return r.count != null ? r.count : r.unread; },
    markMailRead: function (id) { return request('/mails/' + enc(id) + '/read', { method: 'POST' }); },
    deleteMail: function (id) { return request('/mails/' + enc(id), { method: 'DELETE' }); },
    sendInvite: function (friendId, roomCode) { return request('/mails/invite', { method: 'POST', body: { friendId: Number(friendId), roomCode: roomCode } }); },

    // ── 친선전 룸 (웹 대기실) ─────────────────────────────
    createRoom: function () { return request('/rooms', { method: 'POST', body: { mode: 'friendly' } }); },
    getRoom: function (code) { return request('/rooms/' + enc(code)); },
    joinRoom: function (code) { return request('/rooms/' + enc(code) + '/join', { method: 'POST' }); },
    leaveRoom: function (code) { return request('/rooms/' + enc(code) + '/leave', { method: 'POST' }); },
    readyRoom: function (code, ready) { return request('/rooms/' + enc(code) + '/ready?ready=' + (ready !== false), { method: 'POST' }); },
    kickGuest: function (code) { return request('/rooms/' + enc(code) + '/kick', { method: 'POST' }); },
    startRoom: function (code) { return request('/rooms/' + enc(code) + '/start', { method: 'POST' }); },

    // ── 랭킹전 매칭 대기열 ────────────────────────────────
    //   state: none / searching / matched  (matched 면 opponentNickname, opponentRating, bot)
    joinQueue: function () { return request('/matchmaking/queue', { method: 'POST' }); },
    queueStatus: function () { return request('/matchmaking/status'); },
    leaveQueue: function () { return request('/matchmaking/queue', { method: 'DELETE' }); },

    // ── DB → 클라이언트 실시간 데이터 (Unity 와 같은 API) ──
    liveBootstrap: function () { return request('/live/bootstrap'); },
    liveConfig: function () { return request('/live/config'); },
    /** 실시간 이벤트 주소 (EventSource 는 헤더를 못 붙여서 토큰을 주소에 넣는다) */
    liveEventsUrl: function () { return BASE_URL + '/live/events?access_token=' + encodeURIComponent(getToken() || ''); },

    // ── 관리자 (ROLE_ADMIN) ──────────────────────────────
    adminNotice: function (title, body) { return request('/admin/notices', { method: 'POST', body: { title: title, body: body } }); },
    adminMail: function (m) { return request('/admin/mails', { method: 'POST', body: m }); },
    adminDashboard: function () { return request('/admin/dashboard'); },
    adminUsers: function (q, page, size) { return request('/admin/users?' + new URLSearchParams({ q: q || '', page: page || 0, size: size || 20 })); },
    adminUser: function (userId) { return request('/admin/users/' + userId); },
    adminSetActive: function (userId, active, reason) { return request('/admin/users/' + userId + '/active', { method: 'PUT', body: { active: active, reason: reason || '' } }); },
    adminSetRole: function (userId, role) { return request('/admin/users/' + userId + '/role', { method: 'PUT', body: { role: role } }); },
    adminResetPassword: function (userId) { return request('/admin/users/' + userId + '/reset-password', { method: 'POST' }); },
    adminLoginHistory: function (type, userId, limit) {
      const q = new URLSearchParams({ limit: limit || 100 });
      if (type) q.set('type', type);
      if (userId) q.set('userId', userId);
      return request('/admin/login-history?' + q);
    },
    adminAuditLogs: function (limit) { return request('/admin/audit-logs?limit=' + (limit || 100)); },
    adminConfigs: function () { return request('/admin/configs'); },
    adminUpdateConfig: function (key, value) { return request('/admin/configs/' + enc(key), { method: 'PUT', body: { value: String(value) } }); },
    adminLiveStatus: function () { return request('/admin/live/status'); },
    adminBroadcast: function (m) { return request('/admin/live/broadcast', { method: 'POST', body: m }); },
    adminMotion: function () { return request('/admin/motion'); },
    adminDeleteGesture: function (id) { return request('/admin/motion/gestures/' + id, { method: 'DELETE' }); },
    adminDeleteMotion: function (id) { return request('/admin/motion/recordings/' + id, { method: 'DELETE' }); },

    // ── 기타 ─────────────────────────────────────────────
    health: function () { return request('/health'); },
  };
})(window.WOW);
