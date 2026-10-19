package com.wow.repository;

import com.wow.entity.MatchPlayer;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;
import java.util.Optional;

public interface MatchPlayerRepository extends JpaRepository<MatchPlayer, Long> {

    @Query("select mp from MatchPlayer mp join fetch mp.match m " +
            "where mp.player.id = :playerId order by m.endedAt desc, mp.id desc")
    List<MatchPlayer> findRecentByPlayerId(@Param("playerId") Long playerId, Pageable pageable);

    Optional<MatchPlayer> findByMatchIdAndPlayerId(Long matchId, Long playerId);
}
