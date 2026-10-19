package com.wow.repository;

import com.wow.entity.GameConfig;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;

public interface GameConfigRepository extends JpaRepository<GameConfig, String> {

    List<GameConfig> findAllByOrderByCategoryAscKeyAsc();
}
