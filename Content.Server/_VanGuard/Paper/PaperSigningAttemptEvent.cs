namespace Content.Server._VanGuard.Paper;

[ByRefEvent]
public record struct PaperSigningAttemptEvent(EntityUid Document, EntityUid Signer, bool Denied = false);
