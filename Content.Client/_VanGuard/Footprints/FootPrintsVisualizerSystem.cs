using Content.Client.Fluids;
using Content.Shared._VanGuard.Footprints;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.Footprints;

/// <summary>
/// Applies the server-provided footprint state and colour to the footprint sprite.
/// </summary>
public sealed partial class FootprintVisualizerSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FootprintComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<FootprintComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FootprintComponent, AppearanceChangeEvent>(OnAppearanceChanged, after: [typeof(PuddleSystem)]);
    }

    private void OnInit(Entity<FootprintComponent> ent, ref ComponentInit args)
    {
        _sprite.LayerMapReserve(ent.Owner, FootprintSpriteLayer.MainLayer);
        Refresh(ent);
    }

    private void OnShutdown(Entity<FootprintComponent> ent, ref ComponentShutdown args)
    {
        _sprite.LayerMapRemove(ent.Owner, FootprintSpriteLayer.MainLayer);
    }

    private void OnAppearanceChanged(EntityUid uid, FootprintComponent component, ref AppearanceChangeEvent args)
    {
        Refresh((uid, component));
    }

    private void Refresh(Entity<FootprintComponent> ent)
    {
        if (!_sprite.LayerMapTryGet(ent.Owner, FootprintSpriteLayer.MainLayer, out var layer, true))
            return;

        if (_appearance.TryGetData<string>(ent, FootprintVisualParameter.VisualState, out var state))
            _sprite.LayerSetRsi(ent.Owner, layer, ent.Comp.SpritePath, state);

        if (_appearance.TryGetData<Color>(ent, FootprintVisualParameter.TrackColor, out var color))
            _sprite.SetColor(ent.Owner, color);
    }
}
