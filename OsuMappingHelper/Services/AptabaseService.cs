using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for tracking anonymous usage analytics via Aptabase.
/// All data is anonymous and used only to understand feature usage patterns.
/// </summary>
public class AptabaseService : IDisposable
{
    private const string AppKey = "A-SH-2513306072";
    private const string ApiEndpoint = "/api/v0/events";
    
    private readonly HttpClient _httpClient;
    private readonly string _sessionId;
    private readonly string _appVersion;
    private readonly string _osName;
    private readonly string _osVersion;
    private readonly string _locale;
    private bool _isDisposed;
    private bool _isEnabled = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets or sets whether analytics tracking is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// Creates a new instance of the AptabaseService.
    /// </summary>
    public AptabaseService()
    {
        _sessionId = Guid.NewGuid().ToString();
        _appVersion = GetAppVersion();
        _osName = "Windows";
        _osVersion = Environment.OSVersion.Version.ToString();
        _locale = System.Globalization.CultureInfo.CurrentCulture.Name;

        var baseUrl = GetBaseUrl(AppKey);
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("App-Key", AppKey);
    }

    /// <summary>
    /// Tracks an event with optional properties.
    /// </summary>
    /// <param name="eventName">The name of the event to track.</param>
    /// <param name="props">Optional dictionary of properties to include with the event.</param>
    public void TrackEvent(string eventName, Dictionary<string, object>? props = null)
    {
        if (!_isEnabled || _isDisposed)
            return;

        // Fire and forget - don't block the caller
        _ = TrackEventAsync(eventName, props);
    }

    /// <summary>
    /// Tracks an event asynchronously.
    /// </summary>
    private async Task TrackEventAsync(string eventName, Dictionary<string, object>? props)
    {
        try
        {
            var payload = new EventPayload
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                SessionId = _sessionId,
                EventName = eventName,
                SystemProps = new SystemProps
                {
                    IsDebug = false,
                    OsName = _osName,
                    OsVersion = _osVersion,
                    Locale = _locale,
                    AppVersion = _appVersion,
                    AppBuildNumber = _appVersion,
                    SdkVersion = "aptabase-csharp@0.1.0"
                },
                Props = props
            };

            var events = new[] { payload };
            
            using var response = await _httpClient.PostAsJsonAsync(ApiEndpoint, events, JsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                Logger.Info($"[Aptabase] Failed to track event '{eventName}': {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            // Silently fail - analytics should never break the app
            Logger.Info($"[Aptabase] Error tracking event '{eventName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the base URL for the Aptabase API based on the app key region.
    /// </summary>
    private static string GetBaseUrl(string appKey)
    {
        return "https://analytics.c4tx.top";
    }

    /// <summary>
    /// Gets the application version from version.txt.
    /// </summary>
    private static string GetAppVersion()
    {
        try
        {
            var exePath = Assembly.GetExecutingAssembly().Location;
            var exeDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
            var versionPath = Path.Combine(exeDir, "version.txt");
            
            if (File.Exists(versionPath))
            {
                return File.ReadAllText(versionPath).Trim();
            }
        }
        catch
        {
            // Ignore errors reading version
        }
        
        return "unknown";
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _httpClient.Dispose();
    }

    #region Payload Classes

    private class EventPayload
    {
        public string Timestamp { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public SystemProps SystemProps { get; set; } = new();
        public Dictionary<string, object>? Props { get; set; }
    }

    private class SystemProps
    {
        public bool IsDebug { get; set; }
        public string OsName { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string Locale { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public string AppBuildNumber { get; set; } = string.Empty;
        public string SdkVersion { get; set; } = string.Empty;
    }

    #endregion

    #region Convenience Methods for Common Events

    /// <summary>
    /// Tracks application startup.
    /// </summary>
    /// <param name="trainingMode">Whether the app was started in training mode.</param>
    public void TrackAppStarted(bool trainingMode = false)
    {
        TrackEvent("app_started", new Dictionary<string, object>
        {
            ["training_mode"] = trainingMode,
            ["version"] = _appVersion
        });
    }

    /// <summary>
    /// Tracks BPM analysis being performed.
    /// </summary>
    /// <param name="bpmFactor">The BPM factor used (e.g., "1x", "1/2x").</param>
    public void TrackBpmAnalysis(string bpmFactor)
    {
        TrackEvent("bpm_analysis", new Dictionary<string, object>
        {
            ["factor"] = bpmFactor
        });
    }

    /// <summary>
    /// Tracks SV normalization being performed.
    /// </summary>
    public void TrackSvNormalization()
    {
        TrackEvent("sv_normalization");
    }

    /// <summary>
    /// Tracks a rate change being applied.
    /// </summary>
    /// <param name="rate">The rate multiplier used.</param>
    /// <param name="isBulk">Whether this was a bulk rate change.</param>
    public void TrackRateChange(double rate, bool isBulk = false)
    {
        TrackEvent("rate_change", new Dictionary<string, object>
        {
            ["rate"] = rate,
            ["is_bulk"] = isBulk
        });
    }

    /// <summary>
    /// Tracks bulk rate change being applied.
    /// </summary>
    /// <param name="minRate">The minimum rate.</param>
    /// <param name="maxRate">The maximum rate.</param>
    /// <param name="step">The step size.</param>
    /// <param name="count">Number of rates created.</param>
    public void TrackBulkRateChange(double minRate, double maxRate, double step, int count)
    {
        TrackEvent("bulk_rate_change", new Dictionary<string, object>
        {
            ["min_rate"] = minRate,
            ["max_rate"] = maxRate,
            ["step"] = step,
            ["count"] = count
        });
    }

    /// <summary>
    /// Tracks offset being applied.
    /// </summary>
    /// <param name="offsetMs">The offset in milliseconds.</param>
    public void TrackOffsetApplied(double offsetMs)
    {
        TrackEvent("offset_applied", new Dictionary<string, object>
        {
            ["offset_ms"] = offsetMs
        });
    }

    /// <summary>
    /// Tracks session tracking start.
    /// </summary>
    public void TrackSessionStart()
    {
        TrackEvent("session_start");
    }

    /// <summary>
    /// Tracks session tracking stop.
    /// </summary>
    /// <param name="durationMinutes">Duration of the session in minutes.</param>
    /// <param name="playCount">Number of plays in the session.</param>
    public void TrackSessionStop(double durationMinutes, int playCount)
    {
        TrackEvent("session_stop", new Dictionary<string, object>
        {
            ["duration_minutes"] = Math.Round(durationMinutes, 1),
            ["play_count"] = playCount
        });
    }

    /// <summary>
    /// Tracks a play being recorded in a session.
    /// </summary>
    /// <param name="accuracy">The accuracy achieved.</param>
    /// <param name="msd">The MSD rating of the map.</param>
    /// <param name="skillset">The dominant skillset.</param>
    public void TrackPlayRecorded(double accuracy, float msd, string skillset)
    {
        TrackEvent("play_recorded", new Dictionary<string, object>
        {
            ["accuracy"] = Math.Round(accuracy, 2),
            ["msd"] = Math.Round(msd, 2),
            ["skillset"] = skillset
        });
    }

    /// <summary>
    /// Tracks map indexing being performed.
    /// </summary>
    /// <param name="mapCount">Number of maps indexed.</param>
    public void TrackMapIndexing(int mapCount)
    {
        TrackEvent("map_indexing", new Dictionary<string, object>
        {
            ["map_count"] = mapCount
        });
    }

    /// <summary>
    /// Tracks a recommended map being selected.
    /// </summary>
    /// <param name="focus">The recommendation focus (e.g., "improve_weakest").</param>
    public void TrackRecommendationSelected(string focus)
    {
        TrackEvent("recommendation_selected", new Dictionary<string, object>
        {
            ["focus"] = focus
        });
    }

    /// <summary>
    /// Tracks an update check.
    /// </summary>
    /// <param name="updateAvailable">Whether an update was available.</param>
    public void TrackUpdateCheck(bool updateAvailable)
    {
        TrackEvent("update_check", new Dictionary<string, object>
        {
            ["update_available"] = updateAvailable
        });
    }

    /// <summary>
    /// Tracks osu! connection state.
    /// </summary>
    /// <param name="connected">Whether osu! is connected.</param>
    public void TrackOsuConnection(bool connected)
    {
        TrackEvent("osu_connection", new Dictionary<string, object>
        {
            ["connected"] = connected
        });
    }

    /// <summary>
    /// Tracks marathon creation.
    /// </summary>
    /// <param name="mapCount">Number of maps in the marathon.</param>
    /// <param name="pauseCount">Number of pause sections.</param>
    /// <param name="totalDurationMinutes">Total duration in minutes.</param>
    public void TrackMarathonCreated(int mapCount, int pauseCount, double totalDurationMinutes)
    {
        TrackEvent("marathon_created", new Dictionary<string, object>
        {
            ["map_count"] = mapCount,
            ["pause_count"] = pauseCount,
            ["duration_minutes"] = Math.Round(totalDurationMinutes, 1)
        });
    }

    #endregion
}
