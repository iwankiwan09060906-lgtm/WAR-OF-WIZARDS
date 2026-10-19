// 관리자 콘솔: 통합 DB(회원·로그인 기록·감사 로그) + 모션 DB(제스처·손 모션·AI 모델)
WOW.page({ auth: true }, async function (ctx) {
  const $ = WOW.$;
  const esc = WOW.esc;

  if (!WOW.isAdmin()) { WOW.goError(403, '관리자 계정으로 로그인해야 사용할 수 있습니다.'); return; }
  $('#admin-name').textContent = ctx.profile.nickname + ' (관리자)';

  const fmtDate = function (iso) { return iso ? new Date(iso).toLocaleString('ko-KR', { hour12: false }) : '-'; };
  const fmtBytes = function (n) { return n > 1048576 ? (n / 1048576).toFixed(1) + ' MB' : n > 1024 ? (n / 1024).toFixed(1) + ' KB' : n + ' B'; };
  const EVENT = { LOGIN_SUCCESS: '로그인 성공', LOGIN_FAIL: '로그인 실패', REGISTER: '회원가입', LOGOUT: '로그아웃', PASSWORD_CHANGE: '비밀번호 변경' };

  // ── 탭 전환 (탭을 처음 열 때 데이터를 불러온다) ─────────────────────
  const loaders = {};
  function openTab(name) {
    WOW.$$('#tabs button').forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-tab') === name); });
    WOW.$$('.tab-page').forEach(function (p) { p.classList.toggle('hidden', p.id !== 'tab-' + name); });
    if (loaders[name]) loaders[name]();
  }
  WOW.$$('#tabs button').forEach(function (b) { b.onclick = function () { openTab(b.getAttribute('data-tab')); }; });

  // ── 대시보드 ───────────────────────────────────────────────────────
  loaders.dashboard = async function () {
    const d = await WOW.api.adminDashboard();
    const kpi = function (v, label) { return '<div class="kpi"><b>' + v + '</b><span>' + label + '</span></div>'; };
    $('#kpis').innerHTML = kpi(d.members, '전체 회원') + kpi(d.onlineNow, '지금 접속') + kpi(d.todayLogins, '오늘 로그인') +
      kpi(d.todaySignups, '오늘 가입') + kpi(d.todayLoginFails, '오늘 로그인 실패') + kpi(d.matches, '누적 대전') +
      kpi(d.gestureSamples, '제스처 샘플') + kpi(d.motionRecordings, '모션 녹화');
    $('#db-cards').innerHTML = d.databases.map(function (db) {
      const tables = Object.keys(db.tables).map(function (t) { return '<span>' + esc(t) + '</span><span>' + db.tables[t] + '</span>'; }).join('');
      return '<div class="panel db-card"><div class="between"><h3>' + esc(db.name) + '</h3>' +
        (db.up ? '<span class="status-up">● 연결됨</span>' : '<span class="status-down">● 연결 실패</span>') + '</div>' +
        '<div class="muted" style="margin:4px 0 10px">' + esc(db.role) + '</div>' +
        (db.up
          ? '<div class="db-meta">계정 <b>' + esc(db.username) + '</b><br>' + esc(db.product) + '<br>' + esc(db.url) + '</div><div class="db-tables">' + tables + '</div>'
          : '<div class="alert">' + esc(db.error || '') + '</div>') + '</div>';
    }).join('');
  };

  // ── 회원 관리 ──────────────────────────────────────────────────────
  let memberQuery = '';
  let memberPage = 0;
  loaders.members = async function () {
    const r = await WOW.api.adminUsers(memberQuery, memberPage, 20);
    $('#member-count').textContent = '검색 결과 ' + r.total + '명';
    $('#page-no').textContent = (r.page + 1) + ' / ' + Math.max(1, Math.ceil(r.total / r.size));
    $('#prev-page').disabled = r.page === 0;
    $('#next-page').disabled = (r.page + 1) * r.size >= r.total;
    $('#member-rows').innerHTML = r.items.length === 0 ? '<tr><td colspan="10" class="muted">회원이 없습니다.</td></tr>' : r.items.map(function (m) {
      return '<tr class="clickable" data-user="' + m.userId + '"><td>' + m.userId + '</td><td>' + esc(m.email) + '</td><td>' + esc(m.nickname) + '</td>' +
        '<td class="' + (m.role === 'ROLE_ADMIN' ? 'tag-admin' : '') + '">' + (m.role === 'ROLE_ADMIN' ? '관리자' : '회원') + '</td>' +
        '<td class="' + (m.active ? 'tag-ok' : 'tag-bad') + '">' + (m.active ? '정상' : '정지') + '</td>' +
        '<td>' + m.rating + '</td><td>' + m.wins + '승 ' + m.losses + '패</td><td>' + fmtDate(m.createdAt) + '</td><td>' + fmtDate(m.lastLoginAt) + '</td>' +
        '<td><button class="btn btn-ghost btn-xs">상세</button></td></tr>';
    }).join('');
  };
  $('#member-search').addEventListener('submit', function (e) { e.preventDefault(); memberQuery = e.target.q.value.trim(); memberPage = 0; loaders.members(); });
  $('#prev-page').onclick = function () { memberPage = Math.max(0, memberPage - 1); loaders.members(); };
  $('#next-page').onclick = function () { memberPage++; loaders.members(); };
  $('#member-rows').addEventListener('click', function (e) {
    const row = e.target.closest('[data-user]');
    if (row) showMember(Number(row.getAttribute('data-user')));
  });

  async function showMember(userId) {
    const d = await WOW.api.adminUser(userId);
    const m = d.member;
    const box = $('#member-detail');
    box.classList.remove('hidden');
    box.innerHTML =
      '<div class="between"><h3 class="display" style="font-size:1.2rem;color:var(--purple-100)">' + esc(m.nickname) + ' <small class="muted">' + esc(m.email) + '</small></h3>' +
      '<button class="btn btn-ghost btn-xs" data-act="close">닫기</button></div>' +
      '<div class="muted" style="margin-top:6px">회원 ID ' + m.userId + ' · 플레이어 ID ' + m.playerId + ' · 대전 ' + d.matches + '회 · ' +
      '제스처 샘플 ' + d.gestureSamples + '개 · 모션 녹화 ' + d.motionRecordings + '개 (모션 DB)</div>' +
      '<div class="detail-actions">' +
      (m.active ? '<button class="btn btn-danger btn-sm" data-act="disable">계정 정지</button>' : '<button class="btn btn-secondary btn-sm" data-act="enable">정지 해제</button>') +
      (m.role === 'ROLE_ADMIN' ? '<button class="btn btn-ghost btn-sm" data-act="user">관리자 권한 해제</button>' : '<button class="btn btn-ghost btn-sm" data-act="admin">관리자로 지정</button>') +
      '<button class="btn btn-ghost btn-sm" data-act="reset">임시 비밀번호 발급</button></div>' +
      '<div id="temp-pw"></div>' +
      '<div class="label" style="margin:12px 0 8px">최근 로그인 기록</div>' + loginTable(d.recentLogins);

    box.onclick = function (e) {
      const act = e.target.getAttribute('data-act');
      if (!act) return;
      const btn = e.target;
      if (act === 'close') { box.classList.add('hidden'); return; }
      if (act === 'disable') {
        const reason = prompt(m.nickname + ' 님의 계정을 정지합니다. 사유를 입력하세요 (선택)');
        if (reason === null) return;
        WOW.run(btn, async function () { await WOW.api.adminSetActive(userId, false, reason); await refresh(); }, '계정을 정지했습니다.');
      }
      if (act === 'enable') WOW.run(btn, async function () { await WOW.api.adminSetActive(userId, true); await refresh(); }, '정지를 해제했습니다.');
      if (act === 'admin' && confirm(m.nickname + ' 님을 관리자로 지정할까요?')) WOW.run(btn, async function () { await WOW.api.adminSetRole(userId, 'ROLE_ADMIN'); await refresh(); }, '관리자로 지정했습니다.');
      if (act === 'user' && confirm('관리자 권한을 해제할까요?')) WOW.run(btn, async function () { await WOW.api.adminSetRole(userId, 'ROLE_USER'); await refresh(); }, '권한을 해제했습니다.');
      if (act === 'reset' && confirm('임시 비밀번호를 발급하면 기존 비밀번호로는 로그인할 수 없습니다. 계속할까요?')) {
        WOW.run(btn, async function () {
          const r = await WOW.api.adminResetPassword(userId);
          $('#temp-pw').innerHTML = '<div class="temp-pw">' + esc(r.tempPassword) + '</div><p class="muted" style="margin-top:6px">' + esc(r.message) + ' (이 창을 닫으면 다시 볼 수 없습니다)</p>';
        });
      }
    };
    async function refresh() { await loaders.members(); await showMember(userId); }
    box.scrollIntoView({ behavior: 'smooth' });
  }

  // ── 로그인 기록 ────────────────────────────────────────────────────
  function loginTable(rows) {
    return '<div class="table-wrap"><table class="admin-table"><thead><tr><th>시각</th><th>종류</th><th>이메일</th><th>사유</th><th>IP</th></tr></thead><tbody>' +
      (rows.length === 0 ? '<tr><td colspan="5" class="muted">기록 없음</td></tr>' : rows.map(function (h) {
        return '<tr><td>' + fmtDate(h.createdAt) + '</td><td class="ev-' + h.eventType + '">' + EVENT[h.eventType] + '</td><td>' + esc(h.email || '') + '</td><td>' + esc(h.reason || '') + '</td><td>' + esc(h.ipAddress || '') + '</td></tr>';
      }).join('')) + '</tbody></table></div>';
  }
  loaders.logins = async function () {
    const rows = await WOW.api.adminLoginHistory($('#login-type').value, null, 200);
    $('#login-rows').innerHTML = rows.length === 0 ? '<tr><td colspan="7" class="muted">기록 없음</td></tr>' : rows.map(function (h) {
      return '<tr><td>' + fmtDate(h.createdAt) + '</td><td class="ev-' + h.eventType + '">' + EVENT[h.eventType] + '</td><td>' + esc(h.email || '') + '</td>' +
        '<td>' + (h.userId || '-') + '</td><td>' + esc(h.reason || '') + '</td><td>' + esc(h.ipAddress || '') + '</td><td title="' + esc(h.userAgent || '') + '">' + esc(h.userAgent || '') + '</td></tr>';
    }).join('');
  };
  $('#login-type').onchange = function () { loaders.logins(); };
  $('#login-refresh').onclick = function () { loaders.logins(); };

  // ── 모션 DB ────────────────────────────────────────────────────────
  function labelBars(list) {
    if (list.length === 0) return '<div class="muted">데이터 없음</div>';
    const max = Math.max.apply(null, list.map(function (x) { return x.count; }));
    return list.map(function (x) {
      return '<div class="label-bar"><span>' + esc(x.label) + '</span><i style="width:' + Math.max(2, x.count / max * 100) + '%"></i><b>' + x.count + '</b></div>';
    }).join('');
  }
  loaders.motion = async function () {
    const d = await WOW.api.adminMotion();
    $('#gesture-labels').innerHTML = labelBars(d.gestureByLabel);
    $('#motion-labels').innerHTML = labelBars(d.motionByLabel);
    $('#motion-rows').innerHTML = d.recentRecordings.length === 0 ? '<tr><td colspan="10" class="muted">녹화 없음</td></tr>' : d.recentRecordings.map(function (m) {
      return '<tr><td>' + m.id + '</td><td>' + fmtDate(m.createdAt) + '</td><td>' + (m.playerId || '-') + '</td><td>' + esc(m.label) + '</td><td>' + esc(m.hand) + '</td>' +
        '<td>' + m.frameCount + '</td><td>' + m.jointCount + '</td><td>' + m.fps + '</td><td>' + esc(m.device || '') + '</td>' +
        '<td><button class="btn btn-danger btn-xs" data-del-motion="' + m.id + '">삭제</button></td></tr>';
    }).join('');
    $('#model-rows').innerHTML = d.models.length === 0 ? '<tr><td colspan="6" class="muted">업로드 이력 없음</td></tr>' : d.models.map(function (m) {
      return '<tr><td>' + fmtDate(m.uploadedAt) + '</td><td>' + esc(m.fileName) + '</td><td>' + esc(m.kind) + '</td><td>' + fmtBytes(m.sizeBytes) + '</td><td>' + m.sampleCount + '</td><td title="' + esc(m.sha256 || '') + '">' + esc((m.sha256 || '').slice(0, 16)) + '…</td></tr>';
    }).join('');
  };
  $('#motion-rows').addEventListener('click', function (e) {
    const id = e.target.getAttribute('data-del-motion');
    if (!id || !confirm('모션 녹화 #' + id + ' 을(를) 삭제할까요? 되돌릴 수 없습니다.')) return;
    WOW.run(e.target, async function () { await WOW.api.adminDeleteMotion(id); await loaders.motion(); }, '삭제했습니다.');
  });

  // ── 공지 · 우편 ────────────────────────────────────────────────────
  let recipientsLoaded = false;
  loaders.notice = async function () {
    if (recipientsLoaded) return;
    recipientsLoaded = true;
    const r = await WOW.api.adminUsers('', 0, 100);
    $('#mail-form [name=to]').innerHTML += r.items.map(function (m) {
      return '<option value="' + m.playerId + '">' + esc(m.nickname) + ' (' + esc(m.email) + ')</option>';
    }).join('');
  };
  $('#notice-form').addEventListener('submit', function (e) {
    e.preventDefault();
    const f = e.target;
    WOW.run(e.submitter, async function () { await WOW.api.adminNotice(f.title.value.trim(), f.body.value.trim()); f.reset(); }, '공지를 등록했습니다.');
  });
  $('#mail-form').addEventListener('submit', function (e) {
    e.preventDefault();
    const f = e.target;
    const to = f.to.value ? Number(f.to.value) : null;
    if (!to && !confirm('전체 플레이어에게 우편을 보낼까요?')) return;
    WOW.run(e.submitter, async function () {
      const r = await WOW.api.adminMail({ toPlayerId: to, title: f.title.value.trim(), body: f.body.value.trim(), reward: f.reward.value.trim() || null });
      f.reset();
      if (r && r.message) WOW.toast(r.message, 'success');
    });
  });

  // ── Unity 연동: 게임 설정 + 실시간 전송 ─────────────────────────────
  let pushTargetsLoaded = false;
  loaders.live = async function () {
    const [configs, status] = await Promise.all([WOW.api.adminConfigs(), WOW.api.adminLiveStatus()]);
    $('#live-count').textContent = status.connectedPlayers;
    $('#config-rows').innerHTML = configs.map(function (c) {
      const input = c.type === 'BOOL'
        ? '<select class="text-input" data-cfg="' + esc(c.key) + '"><option' + (c.value === 'true' ? ' selected' : '') + '>true</option><option' + (c.value === 'false' ? ' selected' : '') + '>false</option></select>'
        : '<input class="text-input" data-cfg="' + esc(c.key) + '" value="' + esc(c.value) + '"' + (c.type === 'NUMBER' ? ' inputmode="decimal" style="width:110px"' : ' style="width:320px"') + ' />';
      return '<tr><td>' + esc(c.category) + '</td><td><code>' + esc(c.key) + '</code></td><td>' + esc(c.description || '') + '</td>' +
        '<td>' + input + '</td><td>v' + c.version + '</td><td><button class="btn btn-primary btn-xs" data-save="' + esc(c.key) + '">저장</button></td></tr>';
    }).join('');
    if (!pushTargetsLoaded) {
      pushTargetsLoaded = true;
      const r = await WOW.api.adminUsers('', 0, 100);
      $('#push-form [name=to]').innerHTML += r.items.map(function (m) {
        return '<option value="' + m.playerId + '">' + esc(m.nickname) + ' (플레이어 ' + m.playerId + ')</option>';
      }).join('');
    }
  };
  $('#live-refresh').onclick = function () { loaders.live(); };
  $('#config-rows').addEventListener('click', function (e) {
    const key = e.target.getAttribute('data-save');
    if (!key) return;
    const value = $('[data-cfg="' + CSS.escape(key) + '"]').value;
    WOW.run(e.target, async function () { await WOW.api.adminUpdateConfig(key, value); await loaders.live(); },
      key + ' = ' + value + ' 저장 · 접속 중인 클라이언트에 전송했습니다.');
  });
  $('#push-form').addEventListener('submit', function (e) {
    e.preventDefault();
    const f = e.target;
    const payload = f.payload.value.trim();
    if (payload) {
      try { JSON.parse(payload); } catch (err) { WOW.toast('payload 는 올바른 JSON 이어야 합니다.', 'error'); return; }
    }
    WOW.run(e.submitter, async function () {
      const r = await WOW.api.adminBroadcast({
        toPlayerId: f.to.value ? Number(f.to.value) : null, type: f.type.value.trim() || 'broadcast',
        title: f.title.value.trim(), message: f.message.value.trim(), payload: payload || null,
      });
      WOW.toast(r.message, 'success');
      f.title.value = ''; f.message.value = ''; f.payload.value = '';
    });
  });

  // ── 감사 로그 ──────────────────────────────────────────────────────
  loaders.audit = async function () {
    const rows = await WOW.api.adminAuditLogs(200);
    $('#audit-rows').innerHTML = rows.length === 0 ? '<tr><td colspan="5" class="muted">기록 없음</td></tr>' : rows.map(function (a) {
      return '<tr><td>' + fmtDate(a.createdAt) + '</td><td>' + esc(a.adminEmail || '') + '</td><td>' + esc(a.action) + '</td>' +
        '<td>' + esc((a.targetType || '') + ' ' + (a.targetId || '')) + '</td><td title="' + esc(a.detail || '') + '">' + esc(a.detail || '') + '</td></tr>';
    }).join('');
  };

  // 탭 로딩 중 오류는 토스트로
  Object.keys(loaders).forEach(function (k) {
    const fn = loaders[k];
    loaders[k] = function () { return fn().catch(function (e) { WOW.toast(e.message, 'error'); }); };
  });

  await loaders.dashboard();
});
