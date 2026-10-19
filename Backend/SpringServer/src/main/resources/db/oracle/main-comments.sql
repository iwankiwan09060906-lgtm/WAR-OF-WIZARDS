-- ① 통합 DB (WOW) 테이블/컬럼 한글 설명 — 서버가 시작할 때 DatabaseInitializer 가 자동 실행 (여러 번 실행해도 안전)
-- SQL Developer 등 DB 도구에서 테이블을 열면 이 설명이 보인다. 새 테이블을 만들면 여기에도 한 줄 추가.

COMMENT ON TABLE  users IS '로그인 계정 (이메일·비밀번호·권한·정지 여부)';
COMMENT ON COLUMN users.email IS '로그인 이메일 (소문자, 중복 불가)';
COMMENT ON COLUMN users.username IS '아이디 (가입 시 비우면 이메일과 같음)';
COMMENT ON COLUMN users.password_hash IS 'BCrypt 해시 (원문 비밀번호는 저장하지 않음)';
COMMENT ON COLUMN users.role IS 'ROLE_USER / ROLE_ADMIN';
COMMENT ON COLUMN users.is_active IS '1=정상, 0=관리자가 정지한 계정 (로그인 불가)';
COMMENT ON COLUMN users.last_login_at IS '마지막 로그인 성공 시각';
COMMENT ON COLUMN users.password_changed_at IS '비밀번호를 마지막으로 바꾼 시각 (가입·변경·관리자 초기화)';

COMMENT ON TABLE  players IS '게임 프로필 (users 1:1) — 닉네임·레이팅·전적';
COMMENT ON COLUMN players.user_id IS 'users.id (봇은 null)';
COMMENT ON COLUMN players.nickname IS '닉네임 (2~12자, 중복 불가)';
COMMENT ON COLUMN players.rating IS '랭킹전 레이팅 (시작 1000)';
COMMENT ON COLUMN players.is_bot IS '1=랭킹 채우기용 봇';

COMMENT ON TABLE  player_decks IS '마법서 덱 — 플레이어별 8칸 (slot_index 0~7)';
COMMENT ON TABLE  skills IS '스킬 19종 마스터 데이터 (S01~S13 일반, H01~H06 고위력)';
COMMENT ON TABLE  matches IS '대전 1판 (ranked 랭킹전 / friendly 친선전 / bot 연습)';
COMMENT ON COLUMN matches.match_token IS '중복 기록 방지 토큰 (같은 토큰은 한 번만 저장)';
COMMENT ON TABLE  match_players IS '대전 참가자별 결과 (승패·레이팅 변화·피해량)';
COMMENT ON TABLE  friendships IS '친구 관계 (PENDING 요청 / ACCEPTED 친구)';
COMMENT ON TABLE  mails IS '우편함 (일반 우편·친선전 초대·보상)';
COMMENT ON TABLE  notices IS '공지사항 (관리자 등록)';
COMMENT ON TABLE  notice_reads IS '공지 읽음 기록 (플레이어별)';
COMMENT ON TABLE  game_rooms IS '친선전 룸 (6자리 코드 = Photon 룸 이름)';
COMMENT ON COLUMN game_rooms.guest_id IS '웹 대기실에 입장한 게스트 players.id';
COMMENT ON COLUMN game_rooms.guest_ready IS '게스트 준비 완료 여부';

COMMENT ON TABLE  login_history IS '로그인·회원가입·로그아웃·비밀번호 변경 기록 (관리자 > 로그인 기록)';
COMMENT ON COLUMN login_history.event_type IS 'LOGIN_SUCCESS / LOGIN_FAIL / REGISTER / LOGOUT / PASSWORD_CHANGE';
COMMENT ON COLUMN login_history.reason IS '실패 사유 (비밀번호 불일치, 정지된 계정, 로그인 잠금 …)';
COMMENT ON COLUMN login_history.email IS '입력한 이메일 (없는 계정이면 user_id 는 null)';
COMMENT ON COLUMN login_history.ip_address IS '접속 IP (프록시 뒤면 X-Forwarded-For 첫 주소)';

COMMENT ON TABLE  admin_audit_log IS '관리자 작업 기록 — 누가 언제 무엇을 바꿨는지';
COMMENT ON COLUMN admin_audit_log.action IS 'USER_DISABLE, USER_ENABLE, USER_ROLE, USER_PASSWORD_RESET, NOTICE_CREATE, MAIL_SEND, GESTURE_DELETE, MOTION_DELETE';

COMMENT ON TABLE  game_configs IS '게임 설정값 — 관리자가 바꾸면 접속 중인 Unity/웹에 실시간 전달 (/api/live/events)';
COMMENT ON COLUMN game_configs.config_key IS '설정 이름 (웹 WOW.RULES 경로와 같음, 예: cooldown.normal)';
COMMENT ON COLUMN game_configs.value_type IS 'NUMBER / TEXT / BOOL';
COMMENT ON COLUMN game_configs.version IS '바뀔 때마다 1 증가 (클라이언트 캐시 비교용)';
