package com.wow.repository;

import com.wow.entity.Player;
import jakarta.persistence.LockModeType;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;
import java.util.Optional;

public interface PlayerRepository extends JpaRepository<Player, Long> {

    Optional<Player> findByUserId(Long userId);

    Optional<Player> findByNicknameIgnoreCase(String nickname);

    boolean existsByNicknameIgnoreCase(String nickname);

    List<Player> findAllByOrderByRatingDescWinsDescIdAsc(Pageable pageable);

    List<Player> findByBotFalse();

    /** 관리자 회원 검색: 이메일·닉네임 부분 일치 (q 는 소문자 %검색어%), 봇 제외 */
    @Query(value = "select p from Player p join fetch p.user u where p.bot = false and "
            + "(lower(u.email) like :q or lower(p.nickname) like :q) order by u.id desc",
            countQuery = "select count(p) from Player p join p.user u where p.bot = false and "
            + "(lower(u.email) like :q or lower(p.nickname) like :q)")
    org.springframework.data.domain.Page<Player> searchMembers(@Param("q") String q, Pageable pageable);

    long countByBotFalse();

    /** 전적 반영 시 동시에 두 요청이 레이팅을 덮어쓰지 않도록 행 잠금 (SELECT ... FOR UPDATE) */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select p from Player p where p.id = :id")
    Optional<Player> findByIdForUpdate(@Param("id") Long id);
}
