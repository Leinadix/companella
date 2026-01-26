using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Companella.Models.Beatmap;

/// <summary>
/// Represents parsed data from an osu! beatmap file.
/// </summary>
public class OsuFile
{
	/// <summary>
	/// Full path to the .osu file.
	/// </summary>
	public string FilePath { get; set; } = string.Empty;

	/// <summary>
	/// Directory containing the .osu file (beatmap folder).
	/// </summary>
	public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

	// [General] section
	public string AudioFilename { get; set; } = string.Empty;
	public int AudioLeadIn { get; set; }
	public int PreviewTime { get; set; } = -1;
	public int Mode { get; set; }

	// [Metadata] section
	public string Title { get; set; } = string.Empty;
	public string TitleUnicode { get; set; } = string.Empty;
	public string Artist { get; set; } = string.Empty;
	public string ArtistUnicode { get; set; } = string.Empty;
	public string Creator { get; set; } = string.Empty;
	public string Version { get; set; } = string.Empty;
	public string Source { get; set; } = string.Empty;
	public string Tags { get; set; } = string.Empty;
	public int BeatmapID { get; set; }
	public int BeatmapSetID { get; set; } = -1;

	// [Difficulty] section
	public double HPDrainRate { get; set; } = 5;
	public double CircleSize { get; set; } = 4;
	public double OverallDifficulty { get; set; } = 5;
	public double ApproachRate { get; set; } = 5;
	public double SliderMultiplier { get; set; } = 1.4;
	public double SliderTickRate { get; set; } = 1;

	// [TimingPoints] section
	public List<TimingPoint> TimingPoints { get; set; } = new();

	// Raw sections for preserving file content
	public Dictionary<string, List<string>> RawSections { get; set; } = new();

	/// <summary>
	/// Gets the full path to the audio file.
	/// </summary>
	public string AudioFilePath => Path.Combine(DirectoryPath, AudioFilename);

	/// <summary>
	/// Gets the display title (Unicode if available, otherwise ASCII).
	/// </summary>
	public string DisplayTitle => !string.IsNullOrEmpty(TitleUnicode) ? TitleUnicode : Title;

	/// <summary>
	/// Gets the display artist (Unicode if available, otherwise ASCII).
	/// </summary>
	public string DisplayArtist => !string.IsNullOrEmpty(ArtistUnicode) ? ArtistUnicode : Artist;

	/// <summary>
	/// Gets a formatted display name for the beatmap.
	/// </summary>
	public string DisplayName => $"{DisplayArtist} - {DisplayTitle} [{Version}]";

	/// <summary>
	/// Gets the title based on preference.
	/// </summary>
	/// <param name="preferRomanized">If true, returns romanized title; otherwise unicode if available.</param>
	public string GetTitle(bool preferRomanized)
	{
		return preferRomanized
			? !string.IsNullOrEmpty(Title) ? Title : TitleUnicode
			: !string.IsNullOrEmpty(TitleUnicode)
				? TitleUnicode
				: Title;
	}

	/// <summary>
	/// Gets the artist based on preference.
	/// </summary>
	/// <param name="preferRomanized">If true, returns romanized artist; otherwise unicode if available.</param>
	public string GetArtist(bool preferRomanized)
	{
		return preferRomanized
			? !string.IsNullOrEmpty(Artist) ? Artist : ArtistUnicode
			: !string.IsNullOrEmpty(ArtistUnicode)
				? ArtistUnicode
				: Artist;
	}

	/// <summary>
	/// Gets a formatted display name based on preference.
	/// </summary>
	/// <param name="preferRomanized">If true, uses romanized metadata; otherwise unicode if available.</param>
	public string GetDisplayName(bool preferRomanized)
	{
		return $"{GetArtist(preferRomanized)} - {GetTitle(preferRomanized)} [{Version}]";
	}

	/// <summary>
	/// Gets the game mode name.
	/// </summary>
	public string ModeName => Mode switch
	{
		0 => "osu!standard",
		1 => "osu!taiko",
		2 => "osu!catch",
		3 => "osu!mania",
		_ => "Unknown"
	};

	/// <summary>
	/// Gets the background image filename from the Events section.
	/// </summary>
	[SuppressMessage("Globalization", "CA1310")]
	public string? BackgroundFilename
	{
		get
		{
			if (!RawSections.TryGetValue("Events", out var events))
				return null;

			foreach (var line in events)
			{
				var trimmed = line.Trim();
				// Background format: 0,0,"filename.jpg",0,0
				// or: 0,0,"filename.jpg"
				if (trimmed.StartsWith("0,0,\""))
				{
					var match = Regex.Match(trimmed, @"0,0,""([^""]+)""");
					if (match.Success)
						return match.Groups[1].Value;
				}
			}

			return null;
		}
	}

	/// <summary>
	/// Gets the full path to the background image file.
	/// </summary>
	public string? BackgroundFilePath
	{
		get
		{
			var filename = BackgroundFilename;
			if (string.IsNullOrEmpty(filename))
				return null;

			var path = Path.Combine(DirectoryPath, filename);
			return File.Exists(path) ? path : null;
		}
	}
}
