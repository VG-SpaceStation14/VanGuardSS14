using System.Numerics;
using Content.Shared._VanGuard.CCVars;
using Content.Shared._VanGuard.Shaders.Bloom;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._VanGuard.Shaders.Bloom;

public sealed partial class LightingOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private EntityQuery<TransformComponent> _transformQuery;

    private ConeLightingOverlay _cone = default!;
    private PointLightingOverlay _point = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "LightingOverlay";

    private bool _bloomEnabled;
    private bool _coneEnabled;

    private float _strength;

    private ConfigurationMultiSubscriptionBuilder _configSub = default!;

    private readonly List<(TransformComponent xform, Matrix3x2 matrix, Vector2 worldPos, Color color)> _entities = [];

    public override void Initialize()
    {
        base.Initialize();

        _cone = new ConeLightingOverlay(_prototypeManager, _sprite, Shader);
        _point = new PointLightingOverlay(_prototypeManager, _sprite, Shader);

        _transformQuery = GetEntityQuery<TransformComponent>();

        _configSub = _cfg.SubscribeMultiple()
            .OnValueChanged(VGCCVars.BloomEnabled, OnBloomEnabledChanged, true)
            .OnValueChanged(VGCCVars.LightBloomConeEnable, OnConeEnabledChanged, true)
            .OnValueChanged(VGCCVars.LightBloomStrength, OnStrengthChanged, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity == null)
            return;

        if (!_bloomEnabled)
            return;

        _entities.Clear();

        var query = EntityQueryEnumerator<BloomOverlayVisualsComponent, PointLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var pointLight, out var xform))
        {
            if (!pointLight.Enabled)
                continue;

            var (worldPos, _, worldMatrix) = _transform.GetWorldPositionRotationMatrix(xform, _transformQuery);

            _entities.Add((xform, worldMatrix, worldPos, pointLight.Color));
        }

        _cone.Entities = _entities;
        _point.Entities = _entities;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay(_cone);
        _cone.Dispose();

        _overlayManager.RemoveOverlay(_point);
        _point.Dispose();

        _configSub.Dispose();
    }

    private void OnBloomEnabledChanged(bool value)
    {
        _bloomEnabled = value;
        UpdateOverlays();
    }

    private void OnConeEnabledChanged(bool value)
    {
        _coneEnabled = value;
        UpdateOverlays();
    }

    private void OnStrengthChanged(float value)
    {
        _strength = Math.Clamp(value, 0.1f, 1f);

        _cone.Strength = _strength;
        _point.Strength = _strength;
    }

    private void UpdateOverlays()
    {
        var shouldEnableCone = _bloomEnabled && _coneEnabled;
        var shouldEnablePoint = _bloomEnabled;

        _cone.Enabled = shouldEnableCone;
        _point.Enabled = shouldEnablePoint;

        ToggleOverlay(shouldEnableCone, _cone);
        ToggleOverlay(shouldEnablePoint, _point);
    }

    private void ToggleOverlay(bool value, Overlay overlay)
    {
        var hasOverlay = _overlayManager.HasOverlay(overlay.GetType());

        if (value && !hasOverlay)
            _overlayManager.AddOverlay(overlay);
        else if (!value && hasOverlay)
            _overlayManager.RemoveOverlay(overlay);
    }
}
