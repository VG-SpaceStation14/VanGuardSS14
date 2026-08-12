using Content.Shared._VanGuard.Language;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._VanGuard.Language;

public sealed partial class LanguageSystem : SharedLanguageSystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public string GetLanguageName(string languageId)
    {
        return _proto.TryIndex<LanguagePrototype>(languageId, out var proto) ? proto.Name : languageId;
    }

    /// <summary>
    ///     Requests a language switch from the server.
    /// </summary>
    public void RequestLanguageSwitch(string languageId)
    {
        if (_proto.TryIndex<LanguagePrototype>(languageId, out _) == false)
            return;

        if (_player.LocalEntity is not { } local)
            return;

        RaiseNetworkEvent(new LanguageChosenMessage(GetNetEntity(local), languageId));
    }
}
