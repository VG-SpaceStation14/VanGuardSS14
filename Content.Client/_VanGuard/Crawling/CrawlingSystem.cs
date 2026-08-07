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
        if (args.Sprite == null)
            return;

        _sprite.SetDrawDepth(uid, _standing.IsDown(uid) ? (int)DrawDepth.SmallMobs : (int)DrawDepth.Mobs);
    }
}