using System.Text.Json.Serialization;
using Robust.Shared.Player;

namespace Content.Server._VanGuard.Discord;

public sealed class VgBanInfo
{
    public string BanId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public uint Minutes { get; set; }
    public DateTimeOffset? Expires { get; set; }

    [JsonIgnore]
    public ICommonSession? Player { get; set; }

    public string PlayerName => Player?.Name ?? string.Empty;

    // Тип бана: Server, Role, Department, Panel
    public string BanType { get; set; } = "Server";

    public Dictionary<string, string> AdditionalInfo { get; set; } = new();
}