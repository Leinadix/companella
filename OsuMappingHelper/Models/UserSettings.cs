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
}
