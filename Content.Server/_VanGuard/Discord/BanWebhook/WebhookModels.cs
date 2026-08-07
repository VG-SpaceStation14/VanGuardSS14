using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Content.Server._VanGuard.Discord;

public sealed class WebhookPayload
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "КАРАТЕЛЬ";

    [JsonPropertyName("embeds")]
    public List<WebhookEmbed> Embeds { get; set; } = new();
}

public sealed class WebhookEmbed
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public uint Color { get; set; }

    [JsonPropertyName("footer")]
    public WebhookEmbedFooter? Footer { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }
}

public sealed class WebhookEmbedFooter
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}