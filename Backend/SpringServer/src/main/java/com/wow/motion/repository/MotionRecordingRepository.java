package com.wow.motion.repository;

import com.wow.motion.entity.MotionRecording;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

import java.util.List;

public interface MotionRecordingRepository extends JpaRepository<MotionRecording, Long> {

    List<MotionRecording> findByIdGreaterThanOrderByIdAsc(Long afterId, Pageable pageable);

    List<MotionRecording> findByLabelAndIdGreaterThanOrderByIdAsc(String label, Long afterId, Pageable pageable);

    List<MotionRecording> findAllByOrderByIdDesc(Pageable pageable);

    long countByPlayerId(Long playerId);

    void deleteByPlayerId(Long playerId);

    @Query("select m.label, count(m) from MotionRecording m group by m.label order by m.label")
    List<Object[]> countByLabel();
}
