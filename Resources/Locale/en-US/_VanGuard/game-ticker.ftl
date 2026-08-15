player-new-join-message = Attention! New player {$name} has joined
    First seen: {$hasSeen ->
        [true] {TOSTRING($firstSeen, "d")}
        *[false] unknown
    }
    Administration should be attentive, this player has less than 10 hours on the server.