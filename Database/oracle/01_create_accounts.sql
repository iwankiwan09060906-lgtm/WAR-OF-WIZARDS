-- ============================================================
-- War of Wizards - Oracle 계정 2개 만들기 (여러 번 실행해도 안전)
--
--   ① 통합 DB  WOW        : 회원 · 로그인 기록 · 게임 데이터 · 관리자 기록
--   ② 모션 DB  WOW_MOTION : 제스처 샘플 · VR 손 모션 녹화 · AI 모델 이력
--
-- 보통은 setup-oracle.bat 이 대신 실행한다. 직접 실행할 때:
--   sqlplus / as sysdba @01_create_accounts.sql
--
-- 테이블·설명(COMMENT)은 Spring 서버가 시작할 때 자동으로 만든다.
-- 비밀번호를 바꾸면 SpringServer/src/main/resources/application-oracle.yml 도 같이 바꿀 것.
-- ============================================================
ALTER SESSION SET CONTAINER = XEPDB1;
SET SERVEROUTPUT ON
WHENEVER SQLERROR EXIT FAILURE

DECLARE
  PROCEDURE create_account(p_user VARCHAR2, p_password VARCHAR2) IS
    v_count NUMBER;
  BEGIN
    SELECT COUNT(*) INTO v_count FROM dba_users WHERE username = UPPER(p_user);
    IF v_count = 0 THEN
      EXECUTE IMMEDIATE 'CREATE USER ' || p_user || ' IDENTIFIED BY "' || p_password || '" DEFAULT TABLESPACE users QUOTA UNLIMITED ON users';
      DBMS_OUTPUT.PUT_LINE('[생성] ' || UPPER(p_user));
    ELSE
      EXECUTE IMMEDIATE 'ALTER USER ' || p_user || ' ACCOUNT UNLOCK';
      DBMS_OUTPUT.PUT_LINE('[있음] ' || UPPER(p_user) || ' (잠금 해제만 함)');
    END IF;
    EXECUTE IMMEDIATE 'GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE, CREATE VIEW TO ' || p_user;
  END;
BEGIN
  create_account('wow', 'wow1234');
  create_account('wow_motion', 'wow1234');
END;
/

SELECT username, account_status, created FROM dba_users WHERE username IN ('WOW', 'WOW_MOTION') ORDER BY username;
EXIT;
