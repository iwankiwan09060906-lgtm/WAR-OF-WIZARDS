package com.wow.config;

import com.zaxxer.hikari.HikariDataSource;
import jakarta.persistence.EntityManagerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.autoconfigure.jdbc.DataSourceProperties;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.boot.orm.jpa.EntityManagerFactoryBuilder;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.data.jpa.repository.config.EnableJpaRepositories;
import org.springframework.orm.jpa.JpaTransactionManager;
import org.springframework.orm.jpa.LocalContainerEntityManagerFactoryBean;
import org.springframework.transaction.PlatformTransactionManager;

import javax.sql.DataSource;
import java.sql.SQLException;

/**
 * ② 모션 DB (Oracle 계정 WOW_MOTION) — 제스처 샘플·VR 손 모션 녹화·AI 모델 이력
 *
 * 크기가 크고(좌표 수천 개) 쓰기가 잦은 학습 데이터를 회원 DB 와 분리해서
 * 백업·삭제·용량 관리를 따로 할 수 있게 한다.
 *
 * 설정: wow.motion.datasource.*  (application-{oracle|h2|postgres}.yml)
 * 엔티티: com.wow.motion.entity  /  리포지토리: com.wow.motion.repository
 * 트랜잭션: 쓰기 메서드에 @Transactional(transactionManager = MotionDbConfig.TX) 를 붙인다.
 *
 * 두 DB 는 서로 외래키로 묶을 수 없으므로 모션 테이블에는 player_id 숫자만 저장한다.
 */
@Configuration
@EnableJpaRepositories(
        basePackages = "com.wow.motion.repository",
        entityManagerFactoryRef = "motionEntityManagerFactory",
        transactionManagerRef = MotionDbConfig.TX)
public class MotionDbConfig {

    /** 모션 DB 트랜잭션 매니저 이름 */
    public static final String TX = "motionTransactionManager";

    @Bean
    @ConfigurationProperties("wow.motion.datasource")
    public DataSourceProperties motionDataSourceProperties() {
        return new DataSourceProperties();
    }

    @Bean
    @ConfigurationProperties("wow.motion.datasource.hikari")
    public HikariDataSource motionDataSource(@Qualifier("motionDataSourceProperties") DataSourceProperties properties) {
        HikariDataSource ds = properties.initializeDataSourceBuilder().type(HikariDataSource.class).build();
        ds.setPoolName("wow-motion");
        return ds;
    }

    @Bean
    public LocalContainerEntityManagerFactoryBean motionEntityManagerFactory(
            EntityManagerFactoryBuilder builder, @Qualifier("motionDataSource") DataSource dataSource) throws SQLException {
        DatabaseConnectionFailureAnalyzer.verify(dataSource);  // 연결 실패 시 원인을 한글로 안내
        return builder.dataSource(dataSource)
                .packages("com.wow.motion.entity")
                .persistenceUnit("motion")
                .build();
    }

    @Bean(name = TX)
    public PlatformTransactionManager motionTransactionManager(
            @Qualifier("motionEntityManagerFactory") EntityManagerFactory entityManagerFactory) {
        return new JpaTransactionManager(entityManagerFactory);
    }
}
