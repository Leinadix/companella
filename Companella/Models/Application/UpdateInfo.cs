using System.Text.Json.Serialization;

namespace Companella.Models.Application;

/// <summary>
/// Represents information about an available update from GitHub.
/// </summary>
public class UpdateInfo
{
    /// <summary>
    /// The version tag name (e.g., "v2", "v2.1").
    /// </summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// The release name/title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The release description/body.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a prerelease.
    /// </summary>
    public bool Prerelease { get; set; }

    /// <summary>
    /// The publication date of the release.
    /// </summary>
    public DateTime PublishedAt { get; set; }

    /// <summary>
    /// The download URL for the Release.zip asset.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// The size of the download in bytes.
    /// </summary>
    public long DownloadSize { get; set; }

    /// <summary>
    /// The URL to view the release on GitHub.
    /// </summary>
    public string HtmlUrl { get; set; } = string.Empty;
}

/// <summary>
/// Represents the GitHub API response for a release.
/// </summary>
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

/// <summary>
/// Represents a release asset from the GitHub API.
/// </summary>
public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
}
