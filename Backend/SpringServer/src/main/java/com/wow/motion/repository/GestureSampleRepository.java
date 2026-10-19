package com.wow.motion.repository;

import com.wow.motion.entity.GestureSample;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

import java.util.List;

public interface GestureSampleRepository extends JpaRepository<GestureSample, Long> {

    List<GestureSample> findByIdGreaterThanOrderByIdAsc(Long afterId, Pageable pageable);

    List<GestureSample> findByLabelAndIdGreaterThanOrderByIdAsc(String label, Long afterId, Pageable pageable);

    @Query("select g.label, count(g) from GestureSample g group by g.label")
    List<Object[]> countByLabel();

    long countByPlayerId(Long playerId);

    List<GestureSample> findByPlayerIdOrderByIdDesc(Long playerId, Pageable pageable);

    void deleteByPlayerId(Long playerId);
}
