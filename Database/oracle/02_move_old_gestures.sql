-- ============================================================
-- (예전 버전에서 업그레이드할 때만) 통합 DB 에 있던 gesture_samples → 모션 DB 로 옮기기
-- setup-oracle.bat 이 매번 실행하지만, 옮길 것이 없으면 아무것도 하지 않는다.
--   - 예전 WOW.GESTURE_SAMPLES 가 없으면: 할 일 없음
--   - 모션 DB 테이블이 아직 없으면: 서버를 한 번 실행한 뒤 setup-oracle.bat 을 다시 실행
-- ============================================================
ALTER SESSION SET CONTAINER = XEPDB1;
SET SERVEROUTPUT ON

DECLARE
  v_old   NUMBER;
  v_new   NUMBER;
  v_moved NUMBER;
BEGIN
  SELECT COUNT(*) INTO v_old FROM dba_tables WHERE owner = 'WOW' AND table_name = 'GESTURE_SAMPLES';
  IF v_old = 0 THEN
    DBMS_OUTPUT.PUT_LINE('[이동] 예전 제스처 테이블 없음 - 건너뜀');
    RETURN;
  END IF;
  SELECT COUNT(*) INTO v_new FROM dba_tables WHERE owner = 'WOW_MOTION' AND table_name = 'GESTURE_SAMPLES';
  IF v_new = 0 THEN
    DBMS_OUTPUT.PUT_LINE('[이동] 모션 DB 테이블이 아직 없음 - 서버를 한 번 실행한 뒤 setup-oracle.bat 을 다시 실행하세요');
    RETURN;
  END IF;

  EXECUTE IMMEDIATE
    'INSERT INTO wow_motion.gesture_samples (player_id, label, point_count, points_json, duration_ms, hand, source, created_at) ' ||
    'SELECT player_id, label, point_count, points_json, duration_ms, hand, source, created_at FROM wow.gesture_samples ORDER BY id';
  v_moved := SQL%ROWCOUNT;
  COMMIT;
  EXECUTE IMMEDIATE 'DROP TABLE wow.gesture_samples CASCADE CONSTRAINTS PURGE';
  DBMS_OUTPUT.PUT_LINE('[이동] 제스처 샘플 ' || v_moved || '건을 모션 DB 로 옮기고 예전 테이블 삭제');
END;
/
EXIT;
