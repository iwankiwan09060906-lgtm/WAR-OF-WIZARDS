package com.wow.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.NoArgsConstructor;
import lombok.Setter;
import org.hibernate.annotations.CreationTimestamp;

import java.time.Instant;

/**
 * 우편함 (가입 보상, 운영 우편, 친선전 초대).
 */
@Entity
@Table(name = "mails")
@Getter
@Setter
@NoArgsConstructor
public class Mail {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "recipient_id", nullable = false)
    private Player recipient;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "sender_id")
    private Player sender;

    @Column(name = "sender_name", length = 20)
    private String senderName;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 10)
    private MailKind kind = MailKind.MAIL;

    @Column(nullable = false, length = 100)
    private String title;

    @Column(nullable = false, length = 1000)
    private String body = "";

    @Column(name = "room_code", length = 6)
    private String roomCode;

    @Column(length = 50)
    private String reward;

    @Column(name = "is_read", nullable = false)
    private boolean read;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    public Mail(Player recipient, MailKind kind, String title, String body) {
        this.recipient = recipient;
        this.kind = kind;
        this.title = title;
        this.body = body;
    }
}
