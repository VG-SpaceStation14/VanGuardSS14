using Content.Corvax.Interfaces.Client;
using Content.Client._VanGuard.Discord.UI;

namespace Content.Client._VanGuard.Sponsors;

public sealed partial class ClientSponsorWindowCreator : ISponsorWindowCreator
{
    public void OpenWindow()
    {
        var window = new DiscordLinkWindow();
        window.OpenCentered();
    }
}