namespace Content.Shared.CartridgeLoader;

/// <summary>
/// Raised when the active cartridge in a cartridge loader changes
/// </summary>
public sealed class CartridgeLoaderActiveCartridgeChangedEvent : EntityEventArgs
{
    public EntityUid? ActiveCartridge { get; }

    public CartridgeLoaderActiveCartridgeChangedEvent(EntityUid? activeCartridge)
    {
        ActiveCartridge = activeCartridge;
    }
}