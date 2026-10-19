package com.wow.repository;

import com.wow.entity.NoticeRead;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;

public interface NoticeReadRepository extends JpaRepository<NoticeRead, Long> {

    boolean existsByNoticeIdAndPlayerId(Long noticeId, Long playerId);

    @Query("select nr.notice.id from NoticeRead nr where nr.player.id = :playerId")
    List<Long> findReadNoticeIds(@Param("playerId") Long playerId);
}
