package com.wow.repository;

import com.wow.entity.Match;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface MatchRepository extends JpaRepository<Match, Long> {

    Optional<Match> findByMatchToken(String matchToken);

    boolean existsByMatchToken(String matchToken);
}
