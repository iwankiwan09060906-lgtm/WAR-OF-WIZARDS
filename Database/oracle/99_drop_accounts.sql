-- ============================================================
-- 주의: 두 계정과 모든 데이터(회원·전적·모션)를 삭제한다. 되돌릴 수 없음!
-- 처음부터 다시 만들 때만:
--   sqlplus / as sysdba @99_drop_accounts.sql   →   setup-oracle.bat
-- (서버가 실행 중이면 먼저 끌 것)
-- ============================================================
ALTER SESSION SET CONTAINER = XEPDB1;
DROP USER wow_motion CASCADE;
DROP USER wow CASCADE;
EXIT;
