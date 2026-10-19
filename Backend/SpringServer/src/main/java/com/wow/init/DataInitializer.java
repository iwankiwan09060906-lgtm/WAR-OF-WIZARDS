package com.wow.init;

import com.wow.config.WowProperties;
import com.wow.entity.Friendship;
import com.wow.entity.FriendshipStatus;
import com.wow.entity.Mail;
import com.wow.entity.MailKind;
import com.wow.entity.Match;
import com.wow.entity.MatchMode;
import com.wow.entity.MatchOutcome;
import com.wow.entity.MatchPlayer;
import com.wow.entity.MatchSource;
import com.wow.entity.Notice;
import com.wow.entity.Player;
import com.wow.entity.Skill;
import com.wow.entity.SkillKind;
import com.wow.entity.SkillTier;
import com.wow.entity.User;
import com.wow.entity.UserRole;
import com.wow.repository.FriendshipRepository;
import com.wow.repository.MailRepository;
import com.wow.repository.MatchRepository;
import com.wow.repository.NoticeRepository;
import com.wow.repository.PlayerRepository;
import com.wow.repository.SkillRepository;
import com.wow.repository.UserRepository;
import com.wow.service.MailService;
import com.wow.service.PresenceService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * 서버 시작 시 기본 데이터 채우기.
 * - 스킬 19종: 매번 코드 기준으로 추가/갱신 (웹 UI src/data/skills.ts 와 동일한 이름·효과·색상)
 * - 공지 2개: 공지가 하나도 없을 때만
 * - 데모 데이터(wow.seed.demo-data=true): 데모 계정·관리자·봇 18명·친구·샘플 전적 — 사용자가 한 명도 없을 때만
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DataInitializer implements ApplicationRunner {

    private final SkillRepository skillRepository;
    private final NoticeRepository noticeRepository;
    private final UserRepository userRepository;
    private final PlayerRepository playerRepository;
    private final FriendshipRepository friendshipRepository;
    private final MatchRepository matchRepository;
    private final MailRepository mailRepository;
    private final MailService mailService;
    private final PresenceService presenceService;
    private final PasswordEncoder passwordEncoder;
    private final WowProperties props;

    @Override
    @Transactional
    public void run(ApplicationArguments args) {
        seedSkills();
        seedNotices();
        if (props.seed() != null && props.seed().demoData()) {
            seedDemoData();
            pinOnlineBots();
        }
    }

    // ── 스킬 19종 ──────────────────────────────────────────────────────────

    private record SkillSeed(String code, String name, SkillTier tier, SkillKind kind, String effect, String color, int damage) {
    }

    // baseDamage [TBD]: 웹 프로토타입 battleSim.ts 의 임시 피해량 (S02 는 초당 피해)
    private static final List<SkillSeed> SKILLS = List.of(
            new SkillSeed("S01", "연막", SkillTier.NORMAL, SkillKind.PROJECTILE, "적중 시 상대 명중률 감소 및 시야 외곽 안개 부여", "#94a3b8", 0),
            new SkillSeed("S02", "독 장판", SkillTier.NORMAL, SkillKind.AREA, "구역 내 초당 지속 피해 누적", "#84cc16", 3),
            new SkillSeed("S03", "파이어볼", SkillTier.NORMAL, SkillKind.AREA, "소범위 집중 즉발 피해", "#f97316", 14),
            new SkillSeed("S04", "화살 세례", SkillTier.NORMAL, SkillKind.AREA, "광범위 다수 분산 피해", "#fbbf24", 8),
            new SkillSeed("S05", "토네이도", SkillTier.NORMAL, SkillKind.PROJECTILE, "경로 상 적 유닛 넉백 및 피해", "#67e8f9", 6),
            new SkillSeed("S06", "번개", SkillTier.NORMAL, SkillKind.AREA, "지정 위치 타격 및 스플래시 피해", "#a78bfa", 16),
            new SkillSeed("S07", "총난사", SkillTier.NORMAL, SkillKind.PROJECTILE, "열 전방 유닛부터 순차 연사 타격", "#ec4899", 12),
            new SkillSeed("S08", "방어 쉴드", SkillTier.NORMAL, SkillKind.BUFF, "피해 흡수 쉴드 생성 (피격 없을 시 HP 회복)", "#22d3ee", 0),
            new SkillSeed("S09", "타워 버프", SkillTier.NORMAL, SkillKind.BUFF, "본인 타워 방어력/공격력 일시 강화 (타워 파괴 시 비활성화)", "#c4b5fd", 0),
            new SkillSeed("S10", "집중", SkillTier.NORMAL, SkillKind.BUFF, "다음 공격 1회의 명중률 및 공격력 대폭 증가", "#facc15", 0),
            new SkillSeed("S11", "도끼 전사", SkillTier.NORMAL, SkillKind.SUMMON, "근접 공격형 소환수 소환", "#dc2626", 0),
            new SkillSeed("S12", "마법사", SkillTier.NORMAL, SkillKind.SUMMON, "원거리 공격형 소환수 소환", "#818cf8", 0),
            new SkillSeed("S13", "터렛", SkillTier.NORMAL, SkillKind.INSTALL, "적 유닛 요격용 고정 포탑 설치 (진영당 1기)", "#71717a", 0),
            new SkillSeed("H01", "프리즈", SkillTier.HIGH, SkillKind.GLOBAL, "적 전체 미니언 및 소환수 5초간 행동 정지", "#38bdf8", 0),
            new SkillSeed("H02", "레이저 빔", SkillTier.HIGH, SkillKind.BEAM, "0.5초 예고선 후 열 전체 관통 타격 (유닛 및 끝의 플레이어)", "#e879f9", 30),
            new SkillSeed("H03", "흡혈", SkillTier.HIGH, SkillKind.PROJECTILE, "적 플레이어/유닛 타격 시 실 피해량 비례 HP 회복", "#be123c", 12),
            new SkillSeed("H04", "반사 결계", SkillTier.HIGH, SkillKind.BUFF, "상대 플레이어의 스킬 공격 1회 무효화 및 일부 피해 반사", "#2dd4bf", 0),
            new SkillSeed("H05", "분노", SkillTier.HIGH, SkillKind.GLOBAL, "필드 내 기존 아군 유닛들의 공격력/공격속도 증가", "#ef4444", 0),
            new SkillSeed("H06", "소환탑", SkillTier.HIGH, SkillKind.INSTALL, "주기적으로 소형 근접 소환수를 생성하는 탑 설치", "#8b5cf6", 0)
    );

    private void seedSkills() {
        int order = 0;
        for (SkillSeed seed : SKILLS) {
            Skill skill = skillRepository.findByCode(seed.code()).orElseGet(Skill::new);
            skill.setCode(seed.code());
            skill.setName(seed.name());
            skill.setTier(seed.tier());
            skill.setKind(seed.kind());
            skill.setEffect(seed.effect());
            skill.setColor(seed.color());
            // [명세] 일반 5초 / 고위력 20초·게임당 3회
            skill.setCooldownSeconds(seed.tier() == SkillTier.HIGH ? 20 : 5);
            skill.setUsesPerGame(seed.tier() == SkillTier.HIGH ? 3 : 0);
            if (skill.getId() == null) {
                skill.setBaseDamage(seed.damage());   // 이미 있는 스킬의 밸런스 수치는 DB 값을 유지
            }
            skill.setSortOrder(order++);
            skillRepository.save(skill);
        }
    }

    // ── 공지 ──────────────────────────────────────────────────────────────

    private void seedNotices() {
        if (noticeRepository.count() > 0) {
            return;
        }
        noticeRepository.save(new Notice("[공지] 프로토타입 테스트 안내",
                "현재 War of Wizards 프로토타입 테스트 중입니다.\n버그는 팀 노션 QA 페이지에 남겨주세요."));
        noticeRepository.save(new Notice("[공지] 스킬 19종 · 덱 규칙",
                "덱은 19종 중 8장, 고위력 스킬은 최대 2장까지 편성할 수 있습니다.\n고위력 스킬은 게임당 스킬별 3회까지 시전 가능합니다."));
    }

    // ── 데모 데이터 (웹 UI mockApi.ts 와 같은 구성) ───────────────────────

    private record BotSeed(String nickname, int rating, int wins, int losses) {
    }

    private static final List<BotSeed> BOTS = List.of(
            new BotSeed("Merlin", 1720, 88, 21), new BotSeed("LunaSpell", 1655, 74, 26),
            new BotSeed("StormCaller", 1590, 61, 30), new BotSeed("FrostWitch", 1522, 55, 31),
            new BotSeed("Gandalf_K", 1470, 49, 33), new BotSeed("Aria", 1415, 44, 35),
            new BotSeed("Nyx", 1362, 40, 36), new BotSeed("Ember", 1310, 37, 35),
            new BotSeed("Orion", 1265, 33, 34), new BotSeed("Selene", 1218, 30, 33),
            new BotSeed("Kael", 1170, 27, 33), new BotSeed("Zephyr", 1122, 24, 31),
            new BotSeed("Mira", 1080, 21, 29), new BotSeed("Thorn", 1035, 18, 27),
            new BotSeed("Vex", 990, 15, 25), new BotSeed("Lyra", 940, 12, 22),
            new BotSeed("Draco", 880, 9, 20), new BotSeed("Iris", 820, 6, 18)
    );

    private static final List<String> ONLINE_BOTS = List.of("Aria", "Mira", "Kael", "Nyx");

    private void seedDemoData() {
        if (userRepository.count() > 0) {
            return;
        }
        log.info("데모 데이터를 생성합니다. (demo@wow.dev / demo1234, admin@wow.dev)");

        Map<String, Player> bots = new HashMap<>();
        for (BotSeed seed : BOTS) {
            bots.put(seed.nickname(), playerRepository.save(Player.bot(seed.nickname(), seed.rating(), seed.wins(), seed.losses())));
        }

        // 관리자
        User adminUser = userRepository.save(new User("admin", "admin@wow.dev",
                passwordEncoder.encode(props.seed().adminPassword()), UserRole.ROLE_ADMIN));
        playerRepository.save(new Player(adminUser, "GameMaster"));

        // 데모 계정 (웹 UI 로그인 화면의 "데모 계정으로 바로 시작")
        User demoUser = userRepository.save(new User("demo@wow.dev", "demo@wow.dev",
                passwordEncoder.encode("demo1234"), UserRole.ROLE_USER));
        Player demo = new Player(demoUser, "DemoWizard");
        demo.setWins(3);
        demo.setLosses(2);
        demo.setExperience(3 * 150 + 2 * 50);
        demo.getDeck().addAll(List.of("S03", "S02", "S07", "S08", "S11", "S13", "H01", "H02"));
        demo = playerRepository.save(demo);

        // 샘플 전적 5판
        String[] modes = {"RANKED", "RANKED", "BOT", "FRIENDLY", "RANKED"};
        String[] results = {"WIN", "LOSE", "WIN", "WIN", "LOSE"};
        String[] opponents = {"Kael", "Merlin", "Bot", "Aria", "Nyx"};
        int rating = 1000;
        for (int i = 0; i < 5; i++) {
            MatchMode mode = MatchMode.valueOf(modes[i]);
            MatchOutcome outcome = MatchOutcome.valueOf(results[i]);
            int delta = mode == MatchMode.RANKED ? (outcome == MatchOutcome.WIN ? 16 : -16) : 0;
            int duration = 300 + i * 95;
            Instant endedAt = Instant.now().minus((long) (5 - i) * 7, ChronoUnit.HOURS).truncatedTo(ChronoUnit.SECONDS);
            Match match = new Match(UUID.randomUUID().toString(), mode, MatchSource.CLIENT, null,
                    endedAt.minusSeconds(duration), endedAt, duration);
            if (outcome == MatchOutcome.WIN) {
                match.setWinner(demo);
            }
            match.addPlayer(new MatchPlayer(demo, opponents[i], outcome, 24 + i * 6, 120 + i * 20, 90 + i * 15,
                    rating, rating + delta));
            rating += delta;
            matchRepository.save(match);
        }
        demo.setRating(rating);

        // 친구: 수락 3 / 받은 요청 1 / 보낸 요청 1
        friendshipRepository.save(new Friendship(demo, bots.get("Aria"), FriendshipStatus.ACCEPTED));
        friendshipRepository.save(new Friendship(bots.get("Kael"), demo, FriendshipStatus.ACCEPTED));
        friendshipRepository.save(new Friendship(demo, bots.get("Mira"), FriendshipStatus.ACCEPTED));
        friendshipRepository.save(new Friendship(bots.get("Zephyr"), demo, FriendshipStatus.PENDING));
        friendshipRepository.save(new Friendship(demo, bots.get("Draco"), FriendshipStatus.PENDING));

        // 우편: 환영 보상 + Aria 의 친선전 초대
        mailService.sendWelcomeMail(demo);
        Mail invite = new Mail(demo, MailKind.INVITE, "Aria 님의 친선전 초대", "Aria 님이 친선전에 초대했습니다.");
        invite.setSender(bots.get("Aria"));
        invite.setSenderName("Aria");
        invite.setRoomCode("AB12CD");
        mailRepository.save(invite);
    }

    private void pinOnlineBots() {
        for (String nickname : ONLINE_BOTS) {
            playerRepository.findByNicknameIgnoreCase(nickname)
                    .filter(Player::isBot)
                    .ifPresent(bot -> presenceService.pin(bot.getId()));
        }
    }
}
