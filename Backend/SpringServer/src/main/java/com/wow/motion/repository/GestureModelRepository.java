package com.wow.motion.repository;

import com.wow.motion.entity.GestureModel;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;

public interface GestureModelRepository extends JpaRepository<GestureModel, Long> {

    List<GestureModel> findTop20ByOrderByIdDesc();
}
