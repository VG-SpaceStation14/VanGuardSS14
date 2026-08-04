namespace Content.Shared._VanGuard.PDA.Events;

[ByRefEvent]
public record struct PdaCtrlClickEvent(EntityUid User, EntityUid Target, bool Handled = false);