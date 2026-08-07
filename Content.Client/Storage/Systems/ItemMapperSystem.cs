using System.Linq;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Sprite; // VG-Tweak
using Robust.Client.GameObjects;
using Robust.Client.Graphics; // VG-Tweak
using Robust.Client.ResourceManagement; // VG-Tweak
using Robust.Shared.Containers; // VG-Tweak
using Robust.Shared.Timing; // VG-Tweak
using Robust.Shared.Utility;

namespace Content.Client.Storage.Systems;

public sealed partial class ItemMapperSystem : SharedItemMapperSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IResourceCache _resourceCache = default!; // VG-Tweak
    [Dependency] private IGameTiming _timing = default!; // VG-Tweak

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemMapperComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemMapperComponent, AppearanceChangeEvent>(OnAppearance);
    }

    // VG-Tweak-Fix Start
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ItemMapperComponent, SpriteComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out var mapper, out var sprite, out var appearance))
        {
            if (mapper.SpriteLayers.Count == 0)
            {
                InitLayers(uid, mapper, sprite, appearance);
                EnableLayers(uid, mapper, sprite, appearance);
                continue;
            }

            UpdateToolLayers(uid, mapper, sprite);
        }
    }
    // VG-Tweak-Fix End

    private void OnStartup(EntityUid uid, ItemMapperComponent component, ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
        {
            component.RSIPath ??= sprite.BaseRSI?.Path;
        }

        if (component.RSIPath != null)
        {
            component.RSIPath = FixPath(component.RSIPath.Value); // VG-Tweak
        }
    }

    private void OnAppearance(EntityUid uid, ItemMapperComponent component, ref AppearanceChangeEvent args)
    {
        if (TryComp<SpriteComponent>(uid, out var spriteComponent))
        {
            if (component.SpriteLayers.Count == 0)
            {
                InitLayers(uid, component, spriteComponent, args.Component);
            }

            EnableLayers(uid, component, spriteComponent, args.Component);
        }
    }

    // VG-Tweak Start
    private void InitLayers(
        EntityUid uid,
        ItemMapperComponent component,
        SpriteComponent spriteComponent,
        AppearanceComponent appearance)
    {
        if (component.RSIPath == null)
            return;

        if (!_appearance.TryGetData<ShowLayerData>(
                uid,
                StorageMapVisuals.InitLayers,
                out var wrapper,
                appearance))
        {
            return;
        }

        var rsi = component.RSIPath.Value;

        foreach (var sprite in wrapper.QueuedEntities)
        {
            if (component.SpriteLayers.Contains(sprite))
                continue;

            component.SpriteLayers.Add(sprite);

            if (_sprite.LayerExists((uid, spriteComponent), sprite))
                continue;

            _sprite.LayerMapReserve((uid, spriteComponent), sprite);
            _sprite.LayerSetSprite(
                (uid, spriteComponent),
                sprite,
                new SpriteSpecifier.Rsi(rsi, sprite));

            _sprite.LayerSetVisible((uid, spriteComponent), sprite, false);
        }

        if (HasState(rsi, "cutters_handle") &&
            !_sprite.LayerExists((uid, spriteComponent), "cutters_handle"))
        {
            CreateCustomLayer(uid, spriteComponent, rsi, "cutters_handle");
        }

        if (HasState(rsi, "screwdriver") &&
            !_sprite.LayerExists((uid, spriteComponent), "screwdriver"))
        {
            CreateCustomLayer(uid, spriteComponent, rsi, "screwdriver");
        }
    }
    // VG-Tweak End

    private void CreateCustomLayer(
        EntityUid uid,
        SpriteComponent sprite,
        ResPath rsi,
        string layer)
    {
        _sprite.LayerMapReserve((uid, sprite), layer);
        _sprite.LayerSetSprite(
            (uid, sprite),
            layer,
            new SpriteSpecifier.Rsi(rsi, layer));
        _sprite.LayerSetVisible((uid, sprite), layer, false);
    }

    // VG-Tweak Start
    private void EnableLayers(
        EntityUid uid,
        ItemMapperComponent component,
        SpriteComponent spriteComponent,
        AppearanceComponent appearance)
    {
        if (!_appearance.TryGetData<ShowLayerData>(
                uid,
                StorageMapVisuals.LayerChanged,
                out var wrapper,
                appearance))
        {
            return;
        }

        foreach (var layerName in component.SpriteLayers)
        {
            var show = wrapper.QueuedEntities.Contains(layerName);

            if (_sprite.LayerExists((uid, spriteComponent), layerName))
            {
                _sprite.LayerSetVisible((uid, spriteComponent), layerName, show);
            }
        }

        UpdateToolLayers(uid, component, spriteComponent); // VG-Tweak-Fix
    }
    // VG-Tweak End

    // VG-Tweak-Fix Start
    /// <summary>
    /// Вынесено в отдельный метод для вызова из Update и EnableLayers
    /// </summary>
    private void UpdateToolLayers(EntityUid uid, ItemMapperComponent component, SpriteComponent spriteComponent)
    {
        bool hasCutters = _sprite.LayerExists((uid, spriteComponent), "cutters_handle");
        bool hasScrewdriver = _sprite.LayerExists((uid, spriteComponent), "screwdriver");

        if (!hasCutters && !hasScrewdriver)
            return;

        if (hasCutters) _sprite.LayerSetVisible((uid, spriteComponent), "cutters_handle", false);
        if (hasScrewdriver) _sprite.LayerSetVisible((uid, spriteComponent), "screwdriver", false);

        if (!TryComp(uid, out ContainerManagerComponent? containers))
            return;

        // Используем словарь Containers напрямую
        if (!containers.Containers.TryGetValue("storagebase", out var container))
            return;

        foreach (var entity in container.ContainedEntities)
        {
            if (!TryComp(entity, out RandomSpriteComponent? randomSprite))
                continue;

            // Проверяем наличие ключа перед доступом
            if (!randomSprite.Selected.TryGetValue("enum.DamageStateVisualLayers.Base", out var data))
                continue;

            // data - это кортеж, обращаемся к полям напрямую
            var color = data.Color ?? Color.White;
            var proto = MetaData(entity).EntityPrototype?.ID ?? "";

            if (proto == "Wirecutter" && hasCutters)
            {
                _sprite.LayerSetVisible((uid, spriteComponent), "cutters_handle", true);
                _sprite.LayerSetColor((uid, spriteComponent), "cutters_handle", color);
            }

            if (proto == "Screwdriver" && hasScrewdriver)
            {
                _sprite.LayerSetVisible((uid, spriteComponent), "screwdriver", true);
                _sprite.LayerSetColor((uid, spriteComponent), "screwdriver", color);
            }
        }
    }
    // VG-Tweak-Fix End

    private bool HasState(ResPath rsiPath, string state)
    {
        var fixedPath = FixPath(rsiPath);

        if (_resourceCache.TryGetResource<RSIResource>(
                fixedPath,
                out var rsiResource))
        {
            return rsiResource.RSI.TryGetState(state, out _);
        }

        return false;
    }

    private ResPath FixPath(ResPath path)
    {
        var pathString = path.ToString();

        if (!pathString.StartsWith("/"))
            pathString = "/" + pathString;

        if (!pathString.StartsWith("/Textures") &&
            !pathString.StartsWith("/Icons"))
        {
            return new ResPath("/Textures" + pathString);
        }

        return new ResPath(pathString);
    }
    // VG-Tweak End
}