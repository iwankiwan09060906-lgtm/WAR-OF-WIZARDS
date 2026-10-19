-- ② 모션 DB (WOW_MOTION) 테이블/컬럼 한글 설명 + 통합 DB 계정 읽기 권한 — 서버 시작 시 자동 실행

COMMENT ON TABLE  gesture_samples IS '룬 제스처 학습 샘플 (Unity 궤적 → Python 학습)';
COMMENT ON COLUMN gesture_samples.player_id IS '통합 DB WOW.players.id (DB 가 달라 외래키 없음, Python 업로드는 null)';
COMMENT ON COLUMN gesture_samples.label IS '스킬 코드 S01~H06 = 학습 클래스';
COMMENT ON COLUMN gesture_samples.points_json IS '[x0,y0,z0,x1,y1,z1,...] 1차원 좌표 배열';

COMMENT ON TABLE  motion_recordings IS 'VR 손 모션 녹화 (Quest 3 핸드 트래킹 관절 좌표)';
COMMENT ON COLUMN motion_recordings.player_id IS '통합 DB WOW.players.id (외래키 없음)';
COMMENT ON COLUMN motion_recordings.label IS '동작 이름 (스킬 코드 또는 FIST, POINT 같은 제스처)';
COMMENT ON COLUMN motion_recordings.frames_json IS '프레임×관절×xyz 1차원 배열 (길이 = frame_count × joint_count × 3)';

COMMENT ON TABLE  gesture_models IS 'AI 제스처 모델 업로드 이력 (파일은 서버 디스크, 여기는 버전 기록)';

-- SQL Developer 에서 WOW 계정으로도 모션 데이터를 조회할 수 있게 (예: SELECT * FROM wow_motion.motion_recordings)
GRANT SELECT ON gesture_samples TO wow;
GRANT SELECT ON motion_recordings TO wow;
GRANT SELECT ON gesture_models TO wow;
