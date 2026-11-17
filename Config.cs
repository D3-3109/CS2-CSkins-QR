using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace CS2_CSkins_QR;

public class CS2_CSkins_QRConfig : BasePluginConfig
{
    private const int CurrentConfigVersion = 3;
    private const int DefaultRequestTimeoutSeconds = 8;
    private const int DefaultDisplayDurationSeconds = 60;
    private const int DefaultCooldownSeconds = 3;

    public override int Version { get; set; } = CurrentConfigVersion;

    [JsonPropertyName("WebUrl")]
    public string WebUrl { get; set; } = "https://your-skin-service.com";

    [JsonPropertyName("ApiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("RequestTimeoutSeconds")]
    public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;

    [JsonPropertyName("DisplayDurationSeconds")]
    public int DisplayDurationSeconds { get; set; } = DefaultDisplayDurationSeconds;

    [JsonPropertyName("CooldownSeconds")]
    public int CooldownSeconds { get; set; } = DefaultCooldownSeconds;
}
