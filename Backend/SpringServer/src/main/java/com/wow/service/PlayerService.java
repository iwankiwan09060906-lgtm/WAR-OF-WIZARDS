package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.player.ProfileResponse;
import com.wow.dto.player.RankEntryResponse;
import com.wow.dto.server.ServerPlayerInfo;
import com.wow.dto.server.TokenVerifyResponse;
import com.wow.entity.Player;
import com.wow.repository.PlayerRepository;
import com.wow.security.JwtTokenProvider;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;

@Service
@RequiredArgsConstructor
@Transactional(readOnly = true)
public class PlayerService {

    /** 닉네임 규칙 (웹 UI 회원가입 화면과 동일): 2~12자, 한글·영문·숫자·_ */
    private static final Pattern NICKNAME = Pattern.compile("^[A-Za-z0-9가-힣_]{2,12}$");

    private final PlayerRepository playerRepository;
    private final PresenceService presenceService;
    private final JwtTokenProvider tokenProvider;

    public Player getPlayer(Long playerId) {
        return playerRepository.findById(playerId)
                .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다."));
    }

    public ProfileResponse getProfile(Long playerId) {
        return toProfile(getPlayer(playerId));
    }

    public List<RankEntryResponse> getRanking(int limit) {
        int safeLimit = Math.min(Math.max(limit, 1), 100);
        List<Player> players = playerRepository.findAllByOrderByRatingDescWinsDescIdAsc(PageRequest.of(0, safeLimit));
        List<RankEntryResponse> result = new ArrayList<>();
        for (int i = 0; i < players.size(); i++) {
            Player p = players.get(i);
            result.add(new RankEntryResponse(i + 1, p.getId(), p.getNickname(), p.getRating(),
                    p.getWins(), p.getLosses(), winRate(p), tierOf(p.getRating()), p.isBot()));
        }
        return result;
    }

    @Transactional
    public ProfileResponse updateNickname(Long playerId, String nickname) {
        Player player = getPlayer(playerId);
        String nick = validateNickname(nickname);
        if (!player.getNickname().equalsIgnoreCase(nick) && playerRepository.existsByNicknameIgnoreCase(nick)) {
            throw ApiException.conflict("이미 사용 중인 닉네임입니다.");
        }
        player.setNickname(nick);
        return toProfile(player);
    }

    /** 게임 서버용: 플레이어 정보 + 덱 */
    public ServerPlayerInfo getServerInfo(Long playerId) {
        Player p = getPlayer(playerId);
        return new ServerPlayerInfo(p.getId(), p.getNickname(), p.getRating(), p.getLevel(), p.isBot(), List.copyOf(p.getDeck()));
    }

    /** 게임 서버용: 클라이언트가 보낸 로그인 토큰 확인 */
    public TokenVerifyResponse verifyToken(String token) {
        return tokenProvider.parse(token)
                .filter(claims -> claims.playerId() != null)
                .flatMap(claims -> playerRepository.findById(claims.playerId()))
                .map(p -> new TokenVerifyResponse(true, p.getId(), p.getNickname(), p.getRating()))
                .orElse(TokenVerifyResponse.invalid());
    }

    /** 규칙에 맞으면 앞뒤 공백을 뺀 닉네임을, 아니면 400 오류 */
    public static String validateNickname(String nickname) {
        String nick = nickname == null ? "" : nickname.trim();
        if (!NICKNAME.matcher(nick).matches()) {
            throw ApiException.badRequest("닉네임은 2~12자, 한글·영문·숫자·_ 만 사용할 수 있습니다.");
        }
        return nick;
    }

    public static boolean isValidNickname(String nickname) {
        return nickname != null && NICKNAME.matcher(nickname.trim()).matches();
    }

    public ProfileResponse toProfile(Player p) {
        Long userId = p.getUser() != null ? p.getUser().getId() : null;
        return new ProfileResponse(p.getId(), userId, p.getNickname(), p.getRating(),
                p.getWins(), p.getLosses(), p.getDraws(), winRate(p), p.getLevel(), p.getExperience(),
                tierOf(p.getRating()), p.isBot(), presenceService.isOnline(p.getId()), p.getCreatedAt());
    }

    public static double winRate(Player p) {
        int total = p.getTotalGames();
        if (total == 0) {
            return 0.0;
        }
        return Math.round((double) p.getWins() / total * 1000.0) / 10.0;
    }

    public static String tierOf(int rating) {
        if (rating >= 2400) return "Archmage (Grandmaster)";
        if (rating >= 2000) return "High Wizard (Master)";
        if (rating >= 1600) return "Mage (Diamond)";
        if (rating >= 1300) return "Sorcerer (Platinum)";
        if (rating >= 1000) return "Apprentice (Gold)";
        return "Novice (Silver)";
    }
}
