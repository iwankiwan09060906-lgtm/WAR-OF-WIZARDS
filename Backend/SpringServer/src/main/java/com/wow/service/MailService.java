package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.common.ActionResponse;
import com.wow.dto.mail.AdminMailRequest;
import com.wow.dto.mail.MailResponse;
import com.wow.entity.FriendshipStatus;
import com.wow.entity.Mail;
import com.wow.entity.MailKind;
import com.wow.entity.Notice;
import com.wow.entity.NoticeRead;
import com.wow.entity.Player;
import com.wow.repository.FriendshipRepository;
import com.wow.repository.MailRepository;
import com.wow.repository.NoticeReadRepository;
import com.wow.repository.NoticeRepository;
import com.wow.repository.PlayerRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.data.domain.PageRequest;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * 우편함(캔버스 7) — 개인 우편 + 친선전 초대 + 전체 공지를 한 목록으로 돌려준다.
 */
@Service
@RequiredArgsConstructor
public class MailService {

    private static final String MAIL_PREFIX = "mail-";
    private static final String NOTICE_PREFIX = "notice-";

    private final MailRepository mailRepository;
    private final NoticeRepository noticeRepository;
    private final NoticeReadRepository noticeReadRepository;
    private final PlayerRepository playerRepository;
    private final FriendshipRepository friendshipRepository;
    private final LiveEventService liveEventService;

    @Transactional
    public void sendWelcomeMail(Player player) {
        Mail mail = new Mail(player, MailKind.MAIL, "가입을 환영합니다!",
                "마법사 등록을 축하드려요. 마법서에서 덱 8장을 편성하고 첫 대전을 시작해보세요.");
        mail.setReward("골드 500");
        mailRepository.save(mail);
    }

    @Transactional(readOnly = true)
    public List<MailResponse> list(Long me) {
        List<MailResponse> result = new ArrayList<>();
        for (Mail mail : mailRepository.findByRecipientIdOrderByCreatedAtDescIdDesc(me, PageRequest.of(0, 100))) {
            result.add(toResponse(mail));
        }
        Set<Long> readNoticeIds = new HashSet<>(noticeReadRepository.findReadNoticeIds(me));
        for (Notice notice : noticeRepository.findAllByOrderByCreatedAtDescIdDesc(PageRequest.of(0, 50))) {
            result.add(new MailResponse(NOTICE_PREFIX + notice.getId(), "notice", notice.getTitle(), notice.getBody(),
                    null, null, null, readNoticeIds.contains(notice.getId()), notice.getCreatedAt()));
        }
        result.sort(Comparator.comparing(MailResponse::createdAt, Comparator.nullsLast(Comparator.reverseOrder())));
        return result;
    }

    @Transactional(readOnly = true)
    public long unreadCount(Long me) {
        return list(me).stream().filter(m -> !m.read()).count();
    }

    @Transactional
    public ActionResponse markRead(Long me, String id) {
        if (id.startsWith(NOTICE_PREFIX)) {
            Long noticeId = parseId(id.substring(NOTICE_PREFIX.length()));
            Notice notice = noticeRepository.findById(noticeId)
                    .orElseThrow(() -> ApiException.notFound("공지를 찾을 수 없습니다."));
            if (!noticeReadRepository.existsByNoticeIdAndPlayerId(noticeId, me)) {
                Player player = playerRepository.getReferenceById(me);
                noticeReadRepository.save(new NoticeRead(notice, player));
            }
        } else {
            findMyMail(me, id).setRead(true);
        }
        return ActionResponse.ok("읽음 처리했습니다.");
    }

    @Transactional
    public ActionResponse delete(Long me, String id) {
        if (id.startsWith(NOTICE_PREFIX)) {
            throw ApiException.badRequest("공지는 삭제할 수 없습니다.");
        }
        mailRepository.delete(findMyMail(me, id));
        return ActionResponse.ok("우편을 삭제했습니다.");
    }

    /** 친선전 초대 — 친구에게만 보낼 수 있다. roomCode 는 Photon 룸 이름. */
    @Transactional
    public ActionResponse sendInvite(Long me, Long friendId, String roomCode) {
        String code = RoomService.normalize(roomCode);
        if (!RoomService.isValidCode(code)) {
            throw ApiException.badRequest("룸 코드는 영문 대문자/숫자 6자리여야 합니다.");
        }
        boolean friends = friendshipRepository.findBetween(me, friendId)
                .map(f -> f.getStatus() == FriendshipStatus.ACCEPTED)
                .orElse(false);
        if (!friends) {
            throw ApiException.forbidden("친구에게만 초대를 보낼 수 있습니다.");
        }
        Player sender = playerRepository.findById(me)
                .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다."));
        Player recipient = playerRepository.findById(friendId)
                .orElseThrow(() -> ApiException.notFound("친구를 찾을 수 없습니다."));

        Mail invite = new Mail(recipient, MailKind.INVITE, sender.getNickname() + " 님의 친선전 초대",
                sender.getNickname() + " 님이 친선전에 초대했습니다.");
        invite.setSender(sender);
        invite.setSenderName(sender.getNickname());
        invite.setRoomCode(code);
        mailRepository.save(invite);
        liveEventService.sendToPlayer(recipient.getId(), "invite", invite.getTitle(), invite.getBody(),
                java.util.Map.of("roomCode", code, "from", sender.getNickname(), "fromPlayerId", sender.getId()));
        return ActionResponse.ok(recipient.getNickname() + " 님에게 초대장을 보냈습니다. (룸 " + code + ")");
    }

    @Transactional
    public MailResponse createNotice(String title, String body) {
        Notice notice = noticeRepository.save(new Notice(title.trim(), body.trim()));
        liveEventService.broadcast("notice", notice.getTitle(), notice.getBody(), java.util.Map.of("noticeId", NOTICE_PREFIX + notice.getId()));
        return new MailResponse(NOTICE_PREFIX + notice.getId(), "notice", notice.getTitle(), notice.getBody(),
                null, null, null, false, notice.getCreatedAt());
    }

    @Transactional
    public ActionResponse sendAdminMail(AdminMailRequest request) {
        List<Player> recipients;
        if (request.toPlayerId() != null) {
            recipients = List.of(playerRepository.findById(request.toPlayerId())
                    .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다.")));
        } else {
            recipients = playerRepository.findByBotFalse();
        }
        for (Player recipient : recipients) {
            Mail mail = new Mail(recipient, MailKind.MAIL, request.title().trim(),
                    request.body() == null ? "" : request.body().trim());
            mail.setSenderName("운영팀");
            mail.setReward(request.reward() == null || request.reward().isBlank() ? null : request.reward().trim());
            mailRepository.save(mail);
            liveEventService.sendToPlayer(recipient.getId(), "mail", mail.getTitle(), mail.getBody(),
                    mail.getReward() == null ? null : java.util.Map.of("reward", mail.getReward()));
        }
        return ActionResponse.ok(recipients.size() + "명에게 우편을 보냈습니다.");
    }

    private Mail findMyMail(Long me, String id) {
        String raw = id.startsWith(MAIL_PREFIX) ? id.substring(MAIL_PREFIX.length()) : id;
        return mailRepository.findByIdAndRecipientId(parseId(raw), me)
                .orElseThrow(() -> ApiException.notFound("우편을 찾을 수 없습니다."));
    }

    private static Long parseId(String raw) {
        try {
            return Long.parseLong(raw);
        } catch (NumberFormatException ex) {
            throw ApiException.badRequest("잘못된 우편 ID 입니다.");
        }
    }

    private static MailResponse toResponse(Mail mail) {
        String kind = mail.getKind() == MailKind.INVITE ? "invite" : "mail";
        return new MailResponse(MAIL_PREFIX + mail.getId(), kind, mail.getTitle(), mail.getBody(), mail.getSenderName(),
                mail.getRoomCode(), mail.getReward(), mail.isRead(), mail.getCreatedAt());
    }
}
