using Content.Shared._VanGuard.Carrying;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._VanGuard.Carrying;

/// <summary>
/// Consumes the Drop key while carrying and asks the server to release the
/// carried mob. This prevents the local prediction from dropping a normal
/// held item before the server handles Q.
/// </summary>
public sealed class CarryDropInputSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.Drop,
                new PointerInputCmdHandler(OnDropKey), typeof(SharedHandsSystem))
            .Register<CarryDropInputSystem>();
    }

    private bool OnDropKey(ICommonSession? session, EntityCoordinates coords, EntityUid netEntity)
    {
        if (session?.AttachedEntity is not { } carrier || !HasComp<CarryingComponent>(carrier))
            return false;

        RaisePredictiveEvent(new CarryDropRequestEvent());
        return true;
    }
}
