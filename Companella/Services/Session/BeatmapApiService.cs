using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Companella.Services.Common;

namespace Companella.Services.Session;

/// <summary>
/// Service for uploading beatmaps to the beatmap API server.
/// </summary>
public class BeatmapApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly UserSettingsService _settingsService;
    private bool _isDisposed;

    /// <summary>
    /// Gets whether the API is configured and enabled.
    /// </summary>
    public bool IsEnabled => 
        _settingsService.Settings.UploadBeatmapsToServer && 
        !string.IsNullOrWhiteSpace(_settingsService.Settings.BeatmapApiUrl);

    /// <summary>
    /// Gets the configured API URL.
    /// </summary>
    public string ApiUrl => _settingsService.Settings.BeatmapApiUrl;

    public BeatmapApiService(UserSettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Uploads a beatmap file to the server (fire and forget safe).
    /// All exceptions are caught internally - this method never throws.
    /// </summary>
    /// <param name="beatmapPath">Path to the .osu file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if upload was successful, false otherwise.</returns>
    public async Task<BeatmapUploadResult> UploadBeatmapAsync(
        string beatmapPath, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsEnabled)
            {
                return new BeatmapUploadResult 
                { 
                    Success = false, 
                    Error = "Beatmap API upload is not enabled" 
                };
            }

            if (!File.Exists(beatmapPath))
            {
                return new BeatmapUploadResult 
                { 
                    Success = false, 
                    Error = "File not found" 
                };
            }

            var uploadUrl = $"{ApiUrl.TrimEnd('/')}/upload";

            // Read file bytes
            var fileBytes = await File.ReadAllBytesAsync(beatmapPath, cancellationToken);
            var fileName = Path.GetFileName(beatmapPath);

            // Create multipart form content
            using var content = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            // Send request
            var response = await _httpClient.PostAsync(uploadUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<BeatmapUploadResponse>(responseJson);

                return new BeatmapUploadResult
                {
                    Success = true,
                    Md5Hash = result?.Md5Hash,
                    Response = result
                };
            }
            else
            {
                return new BeatmapUploadResult
                {
                    Success = false,
                    Error = $"Server returned {response.StatusCode}"
                };
            }
        }
        catch (OperationCanceledException)
        {
            // Silently ignore cancellations for fire-and-forget
            return new BeatmapUploadResult { Success = false, Error = "Cancelled" };
        }
        catch
        {
            // Silently ignore all errors for fire-and-forget
            return new BeatmapUploadResult { Success = false, Error = "Failed" };
        }
    }

    /// <summary>
    /// Checks if the API server is reachable.
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return false;

        try
        {
            var healthUrl = $"{ApiUrl.TrimEnd('/')}/health";
            var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _httpClient.Dispose();
        _isDisposed = true;
    }
}

/// <summary>
/// Result of a beatmap upload operation.
/// </summary>
public class BeatmapUploadResult
{
    /// <summary>
    /// Whether the upload was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if upload failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// MD5 hash of the uploaded beatmap (from server).
    /// </summary>
    public string? Md5Hash { get; set; }

    /// <summary>
    /// Full response from the server.
    /// </summary>
    public BeatmapUploadResponse? Response { get; set; }
}

/// <summary>
/// Response from the beatmap upload API.
/// </summary>
public class BeatmapUploadResponse
{
    public string? Md5Hash { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Version { get; set; }
    public BeatmapMsdResponse? Msd { get; set; }
    public string? DominantSkillset { get; set; }
}

/// <summary>
/// MSD scores in the upload response.
/// </summary>
public class BeatmapMsdResponse
{
    public float Overall { get; set; }
    public float Stream { get; set; }
    public float Jumpstream { get; set; }
    public float Handstream { get; set; }
    public float Stamina { get; set; }
    public float Jackspeed { get; set; }
    public float Chordjack { get; set; }
    public float Technical { get; set; }
}

