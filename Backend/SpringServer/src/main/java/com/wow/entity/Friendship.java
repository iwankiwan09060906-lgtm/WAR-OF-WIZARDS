package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * 친구 관계. requester → addressee 로 요청하고 addressee 가 수락하면 ACCEPTED.
 * (A→B), (B→A) 중복은 FriendService 에서 막는다.
 */
@Entity
@Table(name = "friendships", uniqueConstraints = {
        @UniqueConstraint(name = "uq_friendship_pair", columnNames = {"requester_id", "addressee_id"})
})
@Getter
@Setter
@NoArgsConstructor
public class Friendship {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "requester_id", nullable = false)
    private Player requester;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "addressee_id", nullable = false)
    private Player addressee;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 10)
    private FriendshipStatus status = FriendshipStatus.PENDING;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    public Friendship(Player requester, Player addressee, FriendshipStatus status) {
        this.requester = requester;
        this.addressee = addressee;
        this.status = status;
    }

    public Player other(Long myPlayerId) {
        return requester.getId().equals(myPlayerId) ? addressee : requester;
    }
}
