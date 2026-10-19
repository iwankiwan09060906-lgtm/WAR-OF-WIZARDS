package com.wow.service;

import com.wow.common.ApiException;
import com.wow.dto.common.ActionResponse;
import com.wow.dto.friend.FriendResponse;
import com.wow.entity.Friendship;
import com.wow.entity.FriendshipStatus;
import com.wow.entity.Player;
import com.wow.repository.FriendshipRepository;
import com.wow.repository.PlayerRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;

@Service
@RequiredArgsConstructor
public class FriendService {

    private final FriendshipRepository friendshipRepository;
    private final PlayerRepository playerRepository;
    private final PresenceService presenceService;
    private final LiveEventService liveEventService;

    @Transactional(readOnly = true)
    public List<FriendResponse> list(Long me) {
        return friendshipRepository.findAllInvolving(me).stream()
                .map(f -> {
                    Player other = f.other(me);
                    String relation = f.getStatus() == FriendshipStatus.ACCEPTED ? "friend"
                            : f.getRequester().getId().equals(me) ? "outgoing" : "incoming";
                    String status = presenceService.statusOf(other.getId());
                    return new FriendResponse(other.getId(), other.getNickname(), other.getRating(), relation,
                            !PresenceService.OFFLINE.equals(status), status);
                })
                .toList();
    }

    @Transactional(readOnly = true)
    public List<Long> friendIds(Long me) {
        return friendshipRepository.findAllInvolving(me).stream()
                .filter(f -> f.getStatus() == FriendshipStatus.ACCEPTED)
                .map(f -> f.other(me).getId())
                .toList();
    }

    @Transactional
    public ActionResponse request(Long me, String nickname) {
        Player target = playerRepository.findByNicknameIgnoreCase(nickname.trim())
                .orElseThrow(() -> ApiException.notFound("해당 닉네임의 마법사를 찾을 수 없습니다."));
        if (target.getId().equals(me)) {
            throw ApiException.badRequest("자기 자신에게는 친구 요청을 보낼 수 없습니다.");
        }
        friendshipRepository.findBetween(me, target.getId()).ifPresent(f -> {
            throw ApiException.conflict(f.getStatus() == FriendshipStatus.ACCEPTED
                    ? "이미 친구입니다." : "이미 친구 요청이 진행 중입니다.");
        });
        Player requester = playerRepository.findById(me)
                .orElseThrow(() -> ApiException.notFound("플레이어를 찾을 수 없습니다."));
        friendshipRepository.save(new Friendship(requester, target, FriendshipStatus.PENDING));
        liveEventService.sendToPlayer(target.getId(), "friend", "친구 요청", requester.getNickname() + " 님이 친구 요청을 보냈습니다.",
                java.util.Map.of("action", "request", "playerId", requester.getId(), "nickname", requester.getNickname()));
        return ActionResponse.ok(target.getNickname() + " 님에게 친구 요청을 보냈습니다.");
    }

    @Transactional
    public ActionResponse accept(Long me, Long otherId) {
        Friendship f = findIncomingRequest(me, otherId);
        f.setStatus(FriendshipStatus.ACCEPTED);
        liveEventService.sendToPlayer(f.getRequester().getId(), "friend", "친구 수락",
                f.getAddressee().getNickname() + " 님이 친구 요청을 수락했습니다.",
                java.util.Map.of("action", "accepted", "playerId", f.getAddressee().getId(), "nickname", f.getAddressee().getNickname()));
        return ActionResponse.ok(f.getRequester().getNickname() + " 님과 친구가 되었습니다.");
    }

    @Transactional
    public ActionResponse reject(Long me, Long otherId) {
        friendshipRepository.delete(findIncomingRequest(me, otherId));
        return ActionResponse.ok("친구 요청을 거절했습니다.");
    }

    /** 친구 삭제 / 보낸 요청 취소 */
    @Transactional
    public ActionResponse remove(Long me, Long otherId) {
        Friendship f = friendshipRepository.findBetween(me, otherId)
                .orElseThrow(() -> ApiException.notFound("친구 관계가 없습니다."));
        friendshipRepository.delete(f);
        return ActionResponse.ok("삭제했습니다.");
    }

    @Transactional(readOnly = true)
    public boolean areFriends(Long a, Long b) {
        return friendshipRepository.findBetween(a, b)
                .map(f -> f.getStatus() == FriendshipStatus.ACCEPTED)
                .orElse(false);
    }

    private Friendship findIncomingRequest(Long me, Long otherId) {
        return friendshipRepository.findBetween(me, otherId)
                .filter(f -> f.getStatus() == FriendshipStatus.PENDING && f.getAddressee().getId().equals(me))
                .orElseThrow(() -> ApiException.notFound("처리할 친구 요청이 없습니다."));
    }
}
