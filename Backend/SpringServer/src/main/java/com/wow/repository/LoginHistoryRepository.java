package com.wow.repository;

import com.wow.entity.LoginHistory;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;

import java.time.Instant;
import java.util.List;

public interface LoginHistoryRepository extends JpaRepository<LoginHistory, Long> {

    List<LoginHistory> findAllByOrderByIdDesc(Pageable pageable);

    List<LoginHistory> findByUserIdOrderByIdDesc(Long userId, Pageable pageable);

    List<LoginHistory> findByEventTypeOrderByIdDesc(LoginHistory.EventType type, Pageable pageable);

    long countByEventTypeAndCreatedAtAfter(LoginHistory.EventType type, Instant after);

    /** 로그인 잠금 계산: 이 시각 이후 이 계정의 로그인 실패 횟수 */
    long countByUserIdAndEventTypeAndCreatedAtAfter(Long userId, LoginHistory.EventType type, Instant after);
}
