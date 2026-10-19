package com.wow.web;

import com.wow.config.WowProperties;
import jakarta.servlet.http.HttpServletRequest;
import lombok.extern.slf4j.Slf4j;
import org.springframework.core.io.FileSystemResource;
import org.springframework.http.CacheControl;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.MediaTypeFactory;
import org.springframework.http.ResponseEntity;
import org.springframework.stereotype.Controller;
import org.springframework.web.bind.annotation.GetMapping;

import java.net.URI;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

/**
 * Unity WebGL 빌드를 http://localhost:8080/game/ 으로 서비스한다.
 *
 * Unity 가 압축 빌드(.br / .gz)를 만들면 브라우저가 풀 수 있도록 Content-Encoding 헤더가 꼭 필요한데,
 * 일반 정적 파일 서버는 이 헤더를 붙여주지 않아 "Unable to parse Build/xxx.framework.js.br" 오류가 난다.
 * 여기서 확장자를 보고 Content-Encoding / Content-Type 을 직접 붙인다.
 *
 * 같은 서버(같은 Origin)에서 열리므로 웹 UI 에서 로그인한 토큰(localStorage)을 Unity 가 그대로 읽을 수 있다.
 * (Brotli(.br)는 브라우저 정책상 https 에서만 동작 — 로컬 http 테스트는 Unity 에서 Gzip 압축으로 빌드)
 */
@Slf4j
@Controller
public class WebGLController {

    private static final String PREFIX = "/game/";

    private final Path root;

    public WebGLController(WowProperties props) {
        this.root = Paths.get(props.webgl().path()).toAbsolutePath().normalize();
        log.info("Unity WebGL 빌드 폴더: {} ({})", root, Files.isDirectory(root) ? "있음" : "아직 없음");
    }

    @GetMapping("/game")
    public ResponseEntity<Void> redirectToSlash() {
        return ResponseEntity.status(HttpStatus.FOUND).location(URI.create(PREFIX)).build();
    }

    @GetMapping("/game/**")
    public ResponseEntity<?> serve(HttpServletRequest request) {
        String uri = request.getRequestURI().substring(request.getContextPath().length());
        String relative = URLDecoder.decode(uri.substring(PREFIX.length()), StandardCharsets.UTF_8);
        if (relative.isEmpty() || relative.endsWith("/")) {
            relative = relative + "index.html";
        }

        Path file = root.resolve(relative).normalize();
        if (!file.startsWith(root) || !Files.isRegularFile(file)) {
            if (relative.equals("index.html")) {
                return ResponseEntity.status(HttpStatus.NOT_FOUND)
                        .contentType(new MediaType(MediaType.TEXT_HTML, StandardCharsets.UTF_8))
                        .body(missingBuildPage());
            }
            return ResponseEntity.notFound().build();
        }

        String fileName = file.getFileName().toString();
        String baseName = fileName;
        HttpHeaders headers = new HttpHeaders();
        if (fileName.endsWith(".br")) {
            headers.set(HttpHeaders.CONTENT_ENCODING, "br");
            baseName = fileName.substring(0, fileName.length() - 3);
        } else if (fileName.endsWith(".gz")) {
            headers.set(HttpHeaders.CONTENT_ENCODING, "gzip");
            baseName = fileName.substring(0, fileName.length() - 3);
        }
        headers.setContentType(mediaTypeOf(baseName));
        headers.setCacheControl(CacheControl.noCache());
        return ResponseEntity.ok().headers(headers).body(new FileSystemResource(file));
    }

    private static MediaType mediaTypeOf(String name) {
        if (name.endsWith(".wasm")) {
            return MediaType.parseMediaType("application/wasm");
        }
        if (name.endsWith(".js")) {
            return MediaType.parseMediaType("application/javascript");
        }
        if (name.endsWith(".data") || name.endsWith(".unityweb") || name.endsWith(".mem")) {
            return MediaType.APPLICATION_OCTET_STREAM;
        }
        if (name.endsWith(".html")) {
            return new MediaType(MediaType.TEXT_HTML, StandardCharsets.UTF_8);
        }
        return MediaTypeFactory.getMediaType(name).orElse(MediaType.APPLICATION_OCTET_STREAM);
    }

    private String missingBuildPage() {
        return """
                <!doctype html><html lang="ko"><head><meta charset="utf-8"><title>Unity WebGL 빌드 없음</title></head>
                <body style="font-family:sans-serif;background:#0b0b1a;color:#ddd;padding:40px;line-height:1.6">
                <h2>Unity WebGL 빌드가 아직 없습니다</h2>
                <p>Unity 에서 <b>File &gt; Build Settings &gt; WebGL &gt; Build</b> 로 아래 폴더에 빌드하세요.</p>
                <pre style="background:#1b1b2e;padding:12px">%s</pre>
                <p>로컬(http) 테스트는 Player Settings &gt; Publishing Settings &gt; Compression Format 을 <b>Gzip</b> 으로 두세요.</p>
                <p>빌드 폴더 위치는 application.yml 의 <code>wow.webgl.path</code> (또는 환경변수 WOW_WEBGL_PATH) 로 바꿀 수 있습니다.</p>
                </body></html>
                """.formatted(root);
    }
}
