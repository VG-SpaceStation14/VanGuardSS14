player-new-join-message = Внимание! Зашёл новый игрок {$name}
    Первый заход: {$hasSeen ->
        [true] {TOSTRING($firstSeen, "d")}
        *[false] неизвестно
    }
    Администрации быть внимательней, у данного игрока меньше 10ч на сервере.