using Content.Shared._VanGuard.Quirks.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;

namespace Content.Server._VanGuard.Quirks.Systems;

/// <summary>
///     Keeps the incognito flag on the player's mind: a quirk component is placed on the character when it
///     spawns and the mind outlives the character, so the round-end list can hide the nickname reliably.
/// </summary>
public sealed partial class IncognitoSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IncognitoComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<IncognitoComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnComponentStartup(Entity<IncognitoComponent> ent, ref ComponentStartup args)
    {
        MarkMind(ent.Owner);
    }

    private void OnMindAdded(Entity<IncognitoComponent> ent, ref MindAddedMessage args)
    {
        MarkMind(ent.Owner);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<IncognitoComponent>(args.Mob))
            MarkMind(args.Mob);
    }

    private void MarkMind(EntityUid character)
    {
        if (!_mind.TryGetMind(character, out var mindId, out _))
            return;

        EnsureComp<IncognitoComponent>(mindId);
    }
}
