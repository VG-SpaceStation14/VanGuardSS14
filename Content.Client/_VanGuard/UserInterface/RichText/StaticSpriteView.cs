using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._VanGuard.UserInterface.RichText;

public sealed partial class StaticSpriteView : Control
{
    [Dependency] private IEntityManager _entMan = default!;
    private SharedTransformSystem? _transformSystem;
    private SpriteSystem? _spriteSystem;

    private EntityUid? _entity;
    private SpriteComponent? _cachedSprite;
    private TransformComponent? _cachedXform;
    private Vector2 _spriteSize = new Vector2(32, 32);
    private Vector2 _spriteCenterOffset = Vector2.Zero;

    public Vector2 SpriteScale { get; set; } = Vector2.One;
    public Direction? OverrideDirection { get; set; } = Direction.South;
    public Vector2 SpriteOffset { get; set; } = Vector2.Zero;

    public StaticSpriteView()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = false;
    }

    public void SetEntity(NetEntity netEnt)
    {
        if (_entMan.TryGetEntity(netEnt, out var uid))
            SetEntity(uid);
        else
        {
            _entity = null;
            _cachedSprite = null;
            _cachedXform = null;
        }
    }

    public void SetEntity(EntityUid? uid)
    {
        if (uid == null || !_entMan.TryGetComponent(uid, out SpriteComponent? sprite) || !_entMan.TryGetComponent(uid, out TransformComponent? xform))
        {
            _entity = null;
            _cachedSprite = null;
            _cachedXform = null;
            return;
        }

        _entity = uid;
        _cachedSprite = sprite;
        _cachedXform = xform;

        InvalidateMeasure();
    }

    private void UpdateSize()
    {
        if (_entity == null || _cachedSprite == null) return;

        var spriteBox = _cachedSprite.CalculateRotatedBoundingBox(default, Angle.Zero, Angle.Zero).CalcBoundingBox();

        _spriteCenterOffset = spriteBox.Center * EyeManager.PixelsPerMeter;

        spriteBox = spriteBox.Translated(-spriteBox.Center);

        var scale = SpriteScale * EyeManager.PixelsPerMeter;
        var bl = spriteBox.BottomLeft * scale;
        var tr = spriteBox.TopRight * scale;

        tr = Vector2.Max(tr, Vector2.Zero);
        bl = Vector2.Min(bl, Vector2.Zero);
        tr = Vector2.Max(tr, -bl);
        bl = Vector2.Min(bl, -tr);
        var box = new Box2(bl, tr);

        _spriteSize = box.Size;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        UpdateSize();

        if (!float.IsNaN(SetSize.X) && !float.IsNaN(SetSize.Y))
            return SetSize;

        return _spriteSize;
    }

    protected override void Draw(IRenderHandle renderHandle)
    {
        if (_entity == null || _cachedSprite == null || _cachedXform == null || _entMan.Deleted(_entity.Value))
            return;

        _spriteSystem ??= _entMan.System<SpriteSystem>();
        _transformSystem ??= _entMan.System<SharedTransformSystem>();

        _spriteSystem.ForceUpdate(_entity.Value);

        var world = renderHandle.DrawingHandleWorld;
        var oldModulate = world.Modulate;
        world.Modulate *= Modulate * ActualModulateSelf;

        var stretchVec = Size / _spriteSize;
        var stretch = MathF.Min(stretchVec.X, stretchVec.Y);
        var scale = SpriteScale * UIScale * stretch;

        var position = PixelSize / 2
                       - new Vector2(_spriteCenterOffset.X, -_spriteCenterOffset.Y) * scale 
                       + SpriteOffset * UIScale;

        renderHandle.DrawEntity(
            _entity.Value,
            position,
            scale,
            Angle.Zero,
            Angle.Zero,
            OverrideDirection,
            _cachedSprite,
            _cachedXform,
            _transformSystem
        );

        world.Modulate = oldModulate;
    }
}
