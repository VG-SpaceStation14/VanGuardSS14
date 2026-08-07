parse-minutes-fail = Unable to parse '{$minutes}' as minutes
parse-session-fail = Did not find session for '{$username}'

## Role Timer Commands

# - playtime_addoverall
cmd-playtime_addoverall-desc = Adds the specified minutes to a player's overall playtime
cmd-playtime_addoverall-help = Usage: {$command} <user name> <minutes>
cmd-playtime_addoverall-succeed = Increased overall time for {$username} to {TOSTRING($time, "dddd\\:hh\\:mm")}
cmd-playtime_addoverall-arg-user = <user name>
cmd-playtime_addoverall-arg-minutes = <minutes>
cmd-playtime_addoverall-error-args = Expected exactly two arguments

# - playtime_addrole
cmd-playtime_addrole-desc = Adds the specified minutes to a player's role playtime
cmd-playtime_addrole-help = Usage: {$command} <user name> <role> <minutes>
cmd-playtime_addrole-succeed = Increased role playtime for {$username} / \'{$role}\' to {TOSTRING($time, "dddd\\:hh\\:mm")}
cmd-playtime_addrole-arg-user = <user name>
cmd-playtime_addrole-arg-role = <role>
cmd-playtime_addrole-arg-minutes = <minutes>
cmd-playtime_addrole-error-args = Expected exactly three arguments

# - playtime_getoverall
cmd-playtime_getoverall-desc = Gets the specified minutes for a player's overall playtime
cmd-playtime_getoverall-help = Usage: {$command} <user name>
cmd-playtime_getoverall-success = Overall time for {$username} is {TOSTRING($time, "dddd\\:hh\\:mm")}.
cmd-playtime_getoverall-arg-user = <user name>
cmd-playtime_getoverall-error-args = Expected exactly one argument

# - GetRoleTimer
cmd-playtime_getrole-desc = Gets all or one role timers from a player
cmd-playtime_getrole-help = Usage: {$command} <user name> [role]
cmd-playtime_getrole-no = Found no role timers
cmd-playtime_getrole-role = Role: {$role}, Playtime: {$time}
cmd-playtime_getrole-overall = Overall playtime is {$time}
cmd-playtime_getrole-succeed = Playtime for {$username} is: {TOSTRING($time, "dddd\\:hh\\:mm")}.
cmd-playtime_getrole-arg-user = <user name>
cmd-playtime_getrole-arg-role = <role|'Overall'>
cmd-playtime_getrole-error-args = Expected exactly one or two arguments

# - playtime_save
cmd-playtime_save-desc = Saves the player's playtimes to the DB
cmd-playtime_save-help = Usage: {$command} <user name>
cmd-playtime_save-succeed = Saved playtime for {$username}
cmd-playtime_save-arg-user = <user name>
cmd-playtime_save-error-args = Expected exactly one argument

## 'playtime_flush' command'

cmd-playtime_flush-desc = Flush active trackers to stored in playtime tracking.
cmd-playtime_flush-help = Usage: {$command} [user name]
    This causes a flush to the internal storage only, it does not flush to DB immediately.
    If a user is provided, only that user is flushed.

cmd-playtime_flush-error-args = Expected zero or one arguments
cmd-playtime_flush-arg-user = [user name]
# VG-Tweak Start
# PlayTime Reset All Command
cmd-playtime_resetall-desc = Resets all playtime (overall and all roles) for a player
cmd-playtime_resetall-help = Usage: { $command } <username/guid>
cmd-playtime_resetall-arg-user = Username or GUID
cmd-playtime_resetall-error-args = Invalid number of arguments. Expected: <username/guid>
cmd-playtime_resetall-succeed = Successfully reset ALL playtime for { $username }\nRoles trackers reset: { $rolescount }\nOverall time now: { $overall } minutes
cmd-playtime_resetall-failed = Failed to reset { $count } trackers: { $trackers }
cmd-playtime_resetall-more =  and { $count } more...

# PlayTime Reset Roles Command
cmd-playtime_resetroles-desc = Resets all role playtime for a player (keeps overall time)
cmd-playtime_resetroles-help = Usage: { $command } <username/guid>
cmd-playtime_resetroles-arg-user = Username or GUID
cmd-playtime_resetroles-error-args = Invalid number of arguments. Expected: <username/guid>
cmd-playtime_resetroles-succeed = Successfully reset role playtime for { $username }\nRoles trackers reset: { $rolescount }\nOverall time kept: { $overall } minutes
cmd-playtime_resetroles-failed = Failed to reset { $count } trackers: { $trackers }
cmd-playtime_resetroles-more =  and { $count } more...

# PlayTime Reset Overall Command
cmd-playtime_resetoverall-desc = Resets overall playtime for a player (keeps role time)
cmd-playtime_resetoverall-help = Usage: { $command } <username/guid>
cmd-playtime_resetoverall-arg-user = Username or GUID
cmd-playtime_resetoverall-error-args = Invalid number of arguments. Expected: <username/guid>
cmd-playtime_resetoverall-succeed = Successfully reset overall time for { $username }\nPrevious overall time: { $before } minutes\nCurrent overall time: { $after } minutes\nRole time kept

# VG-Tweak End
