using Content.Shared.Buckle.Components;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._VanGuard.Crawling;

public sealed partial class CrawlingSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StandingStateComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(EntityUid uid, StandingStateComponent comp, AppearanceChangeEvent args)
    {
        UpdateDrawDepth(uid);
    }

    private void UpdateDrawDepth(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        // Buckled entities are handled by the client buckle sorting, which knows
        // whether the strap is a chair or a bed.
        if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
            return;

        if (_standing.IsDown(uid))
        {
            _sprite.SetDrawDepth((uid, sprite), (int) DrawDepth.SmallMobs);
            return;
        }

        if (sprite.DrawDepth == (int) DrawDepth.SmallMobs)
            _sprite.SetDrawDepth((uid, sprite), (int) DrawDepth.Mobs);
    }
}
