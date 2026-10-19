package com.wow.repository;

import com.wow.entity.Friendship;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;
import java.util.Optional;

public interface FriendshipRepository extends JpaRepository<Friendship, Long> {

    @Query("select f from Friendship f join fetch f.requester join fetch f.addressee " +
            "where f.requester.id = :playerId or f.addressee.id = :playerId order by f.createdAt desc")
    List<Friendship> findAllInvolving(@Param("playerId") Long playerId);

    @Query("select f from Friendship f " +
            "where (f.requester.id = :a and f.addressee.id = :b) or (f.requester.id = :b and f.addressee.id = :a)")
    Optional<Friendship> findBetween(@Param("a") Long a, @Param("b") Long b);
}
