using System.Text.Json.Serialization;

namespace OsuMappingHelper.Models;

/// <summary>
/// User settings/preferences for the application.
/// </summary>
public class UserSettings
{
    /// <summary>
    /// Schema version for future compatibility.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Window width in pixels.
    /// </summary>
    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 480;

    /// <summary>
    /// Window height in pixels.
    /// </summary>
    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 810;

    /// <summary>
    /// Window X position in pixels.
    /// </summary>
    [JsonPropertyName("windowX")]
    public int WindowX { get; set; } = 100;

    /// <summary>
    /// Window Y position in pixels.
    /// </summary>
    [JsonPropertyName("windowY")]
    public int WindowY { get; set; } = 100;

    /// <summary>
    /// Window state (Normal, Maximized, etc.).
    /// </summary>
    [JsonPropertyName("windowState")]
    public string WindowState { get; set; } = "Normal";

    /// <summary>
    /// Whether overlay mode is enabled (window follows osu!).
    /// </summary>
    [JsonPropertyName("overlayMode")]
    public bool OverlayMode { get; set; } = false;

    /// <summary>
    /// Keybind for toggling overlay visibility.
    /// Format: "Alt+Q" for Alt+Q key combination
    /// </summary>
    [JsonPropertyName("toggleVisibilityKeybind")]
    public string ToggleVisibilityKeybind { get; set; } = "Alt+Q";

    /// <summary>
    /// Last used rate changer name format.
    /// </summary>
    [JsonPropertyName("rateChangerFormat")]
    public string RateChangerFormat { get; set; } = "[[name]] [[rate]]";

    /// <summary>
    /// Overlay position X offset from default position.
    /// </summary>
    [JsonPropertyName("overlayOffsetX")]
    public int OverlayOffsetX { get; set; } = 0;

    /// <summary>
    /// Overlay position Y offset from default position.
    /// </summary>
    [JsonPropertyName("overlayOffsetY")]
    public int OverlayOffsetY { get; set; } = 0;

    /// <summary>
    /// Whether to automatically start a session when the application starts.
    /// </summary>
    [JsonPropertyName("autoStartSession")]
    public bool AutoStartSession { get; set; } = false;

    /// <summary>
    /// Whether to automatically end the session when the application exits.
    /// </summary>
    [JsonPropertyName("autoEndSession")]
    public bool AutoEndSession { get; set; } = false;

    /// <summary>
    /// Whether to send anonymous usage analytics data.
    /// This helps improve the application by understanding feature usage patterns.
    /// </summary>
    [JsonPropertyName("sendAnalytics")]
    public bool SendAnalytics { get; set; } = true;

    /// <summary>
    /// URL of the beatmap API server for uploading maps during indexing.
    /// Set to empty string to disable server uploads.
    /// </summary>
    [JsonPropertyName("beatmapApiUrl")]
    public string BeatmapApiUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Whether to upload beatmaps to the server during indexing.
    /// </summary>
    [JsonPropertyName("uploadBeatmapsToServer")]
    public bool UploadBeatmapsToServer { get; set; } = false;

    /// <summary>
    /// Cached osu! installation directory path.
    /// Used when osu! is not running to still access files.
    /// </summary>
    [JsonPropertyName("cachedOsuDirectory")]
    public string? CachedOsuDirectory { get; set; }

    /// <summary>
    /// Global UI scale factor (0.5 to 2.0).
    /// Default is 1.0 (100% scale).
    /// </summary>
    [JsonPropertyName("uiScale")]
    public float UIScale { get; set; } = 1.0f;

    /// <summary>
    /// MinaCalc version to use for MSD calculations.
    /// "515" = MinaCalc 5.15 (latest), "505" = MinaCalc 5.05 (legacy).
    /// Default is "515".
    /// </summary>
    [JsonPropertyName("minacalcVersion")]
    public string MinaCalcVersion { get; set; } = "515";
}
