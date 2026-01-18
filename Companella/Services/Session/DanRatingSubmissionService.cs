using System.Net.Http;
using Companella.Services.Common;

namespace Companella.Services.Session;

/// <summary>
/// Service for submitting dan ratings to the remote API.
/// </summary>
public class DanRatingSubmissionService
{
    private const string ApiUrl = "https://msd.c4tx.top/api/ingame";
    private readonly HttpClient _httpClient;

    public DanRatingSubmissionService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Submits a dan rating to the remote API.
    /// </summary>
    /// <param name="beatmapPath">Path to the .osu beatmap file.</param>
    /// <param name="username">The osu! username.</param>
    /// <param name="danLabel">The dan rating label (e.g., "1st Dan", "Alpha").</param>
    /// <param name="accuracy">The accuracy achieved on the map.</param>
    /// <returns>True if submission was successful, false otherwise.</returns>
    public async Task<bool> SubmitRatingAsync(string beatmapPath, string username, string danLabel, double accuracy)
    {
        if (string.IsNullOrEmpty(beatmapPath) || !File.Exists(beatmapPath))
        {
            Logger.Info($"[DanRating] Beatmap file not found: {beatmapPath}");
            return false;
        }

        if (string.IsNullOrEmpty(username))
        {
            Logger.Info("[DanRating] Username is empty, skipping submission");
            return false;
        }

        try
        {
            using var content = new MultipartFormDataContent();
            
            // Add beatmap file
            var fileBytes = await File.ReadAllBytesAsync(beatmapPath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", Path.GetFileName(beatmapPath));

            // Add username
            content.Add(new StringContent(username), "username");

            // Add rating (dan label)
            content.Add(new StringContent(danLabel), "rating");

            // Add accuracy
            content.Add(new StringContent(accuracy.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)), "accuracy");

            Logger.Info($"[DanRating] Submitting rating: {danLabel} for {Path.GetFileName(beatmapPath)} (user: {username}, acc: {accuracy:F2}%)");

            var response = await _httpClient.PostAsync(ApiUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                Logger.Info("[DanRating] Rating submitted successfully");
                return true;
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                Logger.Info($"[DanRating] Submission failed: {response.StatusCode} - {responseBody}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[DanRating] Error submitting rating: {ex.Message}");
            return false;
        }
    }
}
