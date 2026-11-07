using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;

namespace Content.Client._VanGuard.Handover;

public sealed partial class HandoverClientSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new HandoverCursorOverlay(_input, _eye, _player, EntityManager));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<HandoverCursorOverlay>();
        base.Shutdown();
    }
}
