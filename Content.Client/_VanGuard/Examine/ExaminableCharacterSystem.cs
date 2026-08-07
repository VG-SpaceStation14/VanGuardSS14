using Content.Shared._VanGuard.Examine;
using Robust.Client.UserInterface;

namespace Content.Client._VanGuard.Examine;

public sealed partial class ExaminableCharacterSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ExaminableCharacterInfoMessage>(OnExamineInfo);
    }

    private void OnExamineInfo(ExaminableCharacterInfoMessage ev)
    {

    }
}