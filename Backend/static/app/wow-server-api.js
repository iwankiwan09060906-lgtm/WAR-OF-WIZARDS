/*
 * War of Wizards — 웹 UI ↔ 파이썬 서버(wow_server.py) 연결
 *
 * UI 프로토타입(wow-ui.js)은 원래 브라우저 localStorage 에 가짜 데이터를 저장했다(mock).
 * 이 파일은 그 mock 과 "같은 함수 이름 · 같은 반환 모양"으로 서버 API 를 부른다.
 * wow-ui.js 는 window.WOW_SERVER_API 가 있으면 mock 대신 이것을 쓴다.
 *
 * - 로그인 토큰 키 'wow.spring.token' 은 관리자 콘솔(admin.html) · Unity WebGL 과 같이 쓴다.
 * - user.id = 서버의 playerId (프로필 · 덱 · 친구 · 전적 모두 playerId 기준)
 */
(function () {
  'use strict';

  var TOKEN_KEY = 'wow.spring.token';
  var BASE = (window.WOW_SERVER_URL || '').replace(/\/$/, '');

  function getToken() {
    try { return localStorage.getItem(TOKEN_KEY); } catch (e) { return null; }
  }
  function setToken(t) {
    try { t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY); } catch (e) { /* 저장 불가(사생활 모드) */ }
  }

  async function request(method, path, body) {
    var headers = {};
    var token = getToken();
    if (token) headers['Authorization'] = 'Bearer ' + token;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    var res;
    try {
      res = await fetch(BASE + path, { method: method, headers: headers, body: body === undefined ? undefined : JSON.stringify(body) });
    } catch (e) {
      throw new Error('서버에 연결할 수 없습니다. Backend/run_server.bat 로 서버를 켜 주세요.');
    }
    var data = null;
    var text = await res.text();
    if (text) {
      try { data = JSON.parse(text); } catch (e) { data = null; }
    }
    if (!res.ok) {
      if (res.status === 401) setToken(null);
      var err = new Error((data && data.message) || ('서버 오류 (' + res.status + ')'));
      err.status = res.status;
      throw err;
    }
    return data;
  }

  // 서버 시간 "2026-10-13 09:12:00"(UTC) → ISO 문자열
  function iso(t) {
    if (!t) return new Date().toISOString();
    if (/Z$|[+-]\d\d:?\d\d$/.test(t)) return t;
    return t.replace(' ', 'T') + 'Z';
  }

  function toUser(a) {
    return { id: a.playerId, email: a.email, role: a.role || 'ROLE_USER', nickname: a.nickname };
  }

  function toProfile(p) {
    return { id: p.id, nickname: p.nickname, rating: p.rating, wins: p.wins, losses: p.losses };
  }

  var googleScript = null;
  function loadGoogle() {
    if (window.google && window.google.accounts && window.google.accounts.oauth2) return Promise.resolve();
    if (googleScript) return googleScript;
    googleScript = new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = 'https://accounts.google.com/gsi/client';
      s.async = true;
      s.onload = function () { resolve(); };
      s.onerror = function () { googleScript = null; reject(new Error('Google 로그인 스크립트를 불러오지 못했습니다. (인터넷 연결 확인)')); };
      document.head.appendChild(s);
    });
    return googleScript;
  }

  var api = {
    mode: 'server',

    // ── 인증 ────────────────────────────────────────────────────────────
    async getSession() {
      if (!getToken()) return null;
      try {
        var a = await request('GET', '/api/auth/me');
        if (a.accessToken) setToken(a.accessToken);   // 서버가 새 토큰을 주면 갱신 (24시간 연장)
        return a.playerId ? toUser(a) : null;
      } catch (e) {
        if (e.status === 401 || e.status === 404) { setToken(null); return null; }
        throw e;
      }
    },
    async signUp(email, password, nickname) {
      var a = await request('POST', '/api/auth/register', { email: email, password: password, nickname: nickname.trim() });
      setToken(a.accessToken);
      return { user: toUser(a), needsConfirm: false };
    },
    async signIn(email, password) {
      var a = await request('POST', '/api/auth/login', { email: email, password: password });
      setToken(a.accessToken);
      return toUser(a);
    },
    async signInWithGoogle() {
      var cfg = await request('GET', '/api/auth/config');
      if (!cfg.googleClientId) {
        throw new Error('Google 로그인: 서버에 Google 클라이언트 ID 가 아직 없습니다. Docs/WEB_UI_SERVER.md 의 "Google 로그인" 순서대로 설정하세요.');
      }
      await loadGoogle();
      var tokenResponse = await new Promise(function (resolve, reject) {
        var client = window.google.accounts.oauth2.initTokenClient({
          client_id: cfg.googleClientId,
          scope: 'openid email profile',
          callback: function (r) { r && r.access_token ? resolve(r) : reject(new Error('Google 로그인이 취소되었습니다.')); },
          error_callback: function (e) { reject(new Error('Google 로그인 창이 닫혔습니다. (' + ((e && e.type) || '취소') + ')')); }
        });
        client.requestAccessToken();
      });
      var a = await request('POST', '/api/auth/google', { accessToken: tokenResponse.access_token });
      setToken(a.accessToken);
      // UI 는 원래 Supabase 리다이렉트 방식이라, 새로고침하면 저장된 토큰으로 로비에 들어간다.
      window.location.reload();
    },
    async resetPassword(email) {
      var r = await request('POST', '/api/auth/password-reset', { email: email });
      return r && r.message;
    },
    async signOut() {
      try { await request('POST', '/api/auth/logout', {}); } catch (e) { /* 서버가 꺼져 있어도 로그아웃은 된다 */ }
      setToken(null);
    },
    async isNicknameAvailable(nickname) {
      var r = await request('GET', '/api/auth/nickname-available?nickname=' + encodeURIComponent(nickname.trim()));
      return !!r.available;
    },

    // ── 프로필 · 덱 · 전적 · 랭킹 ───────────────────────────────────────
    async getProfile(id) {
      return toProfile(await request('GET', '/api/players/' + id));
    },
    async loadDeck() {
      var r = await request('GET', '/api/decks/me');
      return (r && r.skills) || [];
    },
    async saveDeck(id, skills) {
      await request('PUT', '/api/decks/me', { skills: skills });
    },
    async listMatches(id, limit) {
      var rows = await request('GET', '/api/matches/player/' + id + '?limit=' + (limit || 30));
      return rows.map(function (m) {
        return {
          id: m.id, mode: m.mode, result: String(m.result || '').toLowerCase(), opponent: m.opponent,
          durationSec: m.durationSec, casts: m.casts, ratingDelta: m.ratingDelta, endedAt: iso(m.recordedAt)
        };
      });
    },
    async recordMatch(id, rec) {
      var r = await request('POST', '/api/matches', {
        mode: rec.mode, result: String(rec.result).toUpperCase(), opponent: rec.opponent,
        durationSec: rec.durationSec, casts: rec.casts
      });
      return Object.assign({}, rec, { id: r.matchId, ratingDelta: r.ratingDelta, newRating: r.newRating, endedAt: new Date().toISOString() });
    },
    async getRanking(limit) {
      var rows = await request('GET', '/api/players/ranking?limit=' + (limit || 100));
      return rows.map(function (r, i) {
        return { rank: r.rank || i + 1, userId: r.id, nickname: r.nickname, rating: r.rating, wins: r.wins, losses: r.losses };
      });
    },

    // ── 친구 · 접속 ─────────────────────────────────────────────────────
    async listFriends() {
      var rows = await request('GET', '/api/friends?all=1');
      return rows.map(function (f) {
        return { id: f.friendPlayerId, nickname: f.nickname, rating: f.rating, relation: f.relation, npc: !!f.npc };
      });
    },
    async requestFriend(id, nickname) {
      await request('POST', '/api/friends/requests', { nickname: nickname.trim() });
    },
    async respondFriend(id, requesterId, accept) {
      await request('POST', '/api/friends/respond', { playerId: requesterId, accept: !!accept });
    },
    async removeFriend(id, otherId) {
      await request('DELETE', '/api/friends/player/' + otherId);
    },
    subscribePresence(id, cb) {
      var stopped = false;
      function tick() {
        if (stopped) return;
        request('POST', '/api/presence/heartbeat', {})
          .then(function () { return request('GET', '/api/presence/online'); })
          .then(function (r) { if (!stopped) cb((r && r.online) || []); })
          .catch(function () { /* 다음 주기에 다시 */ });
      }
      tick();
      var timer = setInterval(tick, 15000);
      return function () { stopped = true; clearInterval(timer); };
    },

    // ── 우편 · 공지 · 초대 ─────────────────────────────────────────────
    async listMail() {
      var rows = await request('GET', '/api/mailbox');
      return rows.map(function (m) { return Object.assign({}, m, { createdAt: iso(m.createdAt) }); });
    },
    async markMailRead(id, item) {
      await request('POST', '/api/mailbox/read', { id: item.id });
    },
    async sendInvite(id, friendId, roomCode) {
      await request('POST', '/api/mails/invite', { friendId: friendId, roomCode: roomCode });
    },

    // ── 랭킹전 매칭 (서버 대기열 · 15초 넘으면 봇) ──────────────────────
    async joinQueue() { return request('POST', '/api/matchmaking/queue', {}); },
    async queueStatus() { return request('GET', '/api/matchmaking/status'); },
    async leaveQueue() { return request('DELETE', '/api/matchmaking/queue'); },

    // ── 계정 관리 (마이페이지) ───────────────────────────────────────
    async changeNickname(nickname) { return request('PUT', '/api/players/me', { nickname: nickname.trim() }); },
    async changePassword(currentPassword, newPassword) { return request('PUT', '/api/auth/password', { currentPassword: currentPassword, newPassword: newPassword }); },
    async skills() { return request('GET', '/api/skills'); },

    // ── 관리자 (ROLE_ADMIN) ──────────────────────────────────────────
    admin: {
      dashboard: function () { return request('GET', '/api/admin/dashboard'); },
      users: function (q) { return request('GET', '/api/admin/users' + (q ? '?q=' + encodeURIComponent(q) : '')); },
      configs: function () { return request('GET', '/api/admin/configs'); },
      setConfig: function (key, value) { return request('PUT', '/api/admin/configs/' + encodeURIComponent(key), { value: value }); },
      loginHistory: function () { return request('GET', '/api/admin/login-history'); },
      auditLogs: function () { return request('GET', '/api/admin/audit-logs'); },
      postNotice: function (title, body) { return request('POST', '/api/admin/notices', { title: title, body: body }); },
      broadcast: function (message) { return request('POST', '/api/admin/live/broadcast', { message: message }); }
    },

    // ── 게임 연결 (매칭 → 게임 방 → Unity 접속 · 결과) ─────────────────
    async health() { return request('GET', '/api/health'); },
    async guestLogin(nickname) {
      var a = await request('POST', '/api/auth/guest', { nickname: nickname.trim() });
      setToken(a.accessToken);
      return toUser(a);
    },
    async currentMatch() { var r = await request('GET', '/api/match/current'); return r && r.match; },
    async matchJoin(id) { var r = await request('POST', '/api/match/' + id + '/join', {}); return r.match; },
    async matchResult(id, body) { var r = await request('POST', '/api/match/' + id + '/result', body); return r.match; },
    async matchLeave(id) { var r = await request('POST', '/api/match/' + id + '/leave', {}); return r.match; },

    // ── 친선전 룸 ───────────────────────────────────────────────────────
    async createRoom() { return request('POST', '/api/rooms', {}); },
    async getRoom(code) { return request('GET', '/api/rooms/' + encodeURIComponent(code)); },
    async joinRoom(code) { return request('POST', '/api/rooms/' + encodeURIComponent(code) + '/join', {}); },
    async setReady(code, ready) { return request('POST', '/api/rooms/' + encodeURIComponent(code) + '/ready?ready=' + (ready ? 'true' : 'false'), {}); },
    async startRoom(code) { return request('POST', '/api/rooms/' + encodeURIComponent(code) + '/start', {}); },
    async leaveRoom(code) { return request('POST', '/api/rooms/' + encodeURIComponent(code) + '/leave', {}); },
    async kickGuest(code) { return request('POST', '/api/rooms/' + encodeURIComponent(code) + '/kick', {}); },

    // ── 룬 인식 · 유니티로 마법 보내기 ─────────────────────────────────
    // strokes: [[{x,y},...], ...] (캔버스 좌표, y 아래로 +)
    async recognizeRune(strokes, candidates) {
      var r = await request('POST', '/api/runes/recognize', { strokes: strokes, candidates: candidates, yDown: true });
      return r.result;
    },
    // phase: 'ready'(룬 인식 → 마법진 띄우기) · 'cast'(시전) · 'cancel'
    castSpell(payload) {
      return request('POST', '/api/spells/cast', payload).catch(function () { return null; });
    },
    // 시전 신호를 받을 Unity 주소 + 최근 신호 (게임 화면의 연결 표시용)
    async spellLink() { return request('GET', '/api/spells/recent'); }
  };

  window.WOW_SERVER_API = api;
})();
