package com.wow;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.autoconfigure.security.servlet.UserDetailsServiceAutoConfiguration;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;

// 로그인은 JWT 로 직접 처리하므로 Spring 기본 in-memory 사용자(generated password)는 만들지 않는다.
@SpringBootApplication(exclude = UserDetailsServiceAutoConfiguration.class)
@org.springframework.scheduling.annotation.EnableScheduling   // LiveEventService 연결 유지(ping)
@ConfigurationPropertiesScan
public class WarOfWizardsApplication {

    public static void main(String[] args) {
        SpringApplication.run(WarOfWizardsApplication.class, args);
    }
}
