using System.Globalization;
using Companella.Models.Beatmap;

namespace Companella.Services;

/// <summary>
/// Parses osu! beatmap (.osu) files.
/// </summary>
public class OsuFileParser
{
    /// <summary>
    /// Parses an .osu file and returns the structured data.
    /// </summary>
    public OsuFile Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Beatmap file not found: {filePath}");

        var osuFile = new OsuFile { FilePath = filePath };
        var lines = File.ReadAllLines(filePath);
        
        string currentSection = string.Empty;
        var currentSectionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines and comments at file level
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            // Check for section header
            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                // Save previous section
                if (!string.IsNullOrEmpty(currentSection))
                {
                    osuFile.RawSections[currentSection] = new List<string>(currentSectionLines);
                }

                currentSection = trimmedLine.Trim('[', ']');
                currentSectionLines.Clear();
                continue;
            }

            // Store raw line for section
            currentSectionLines.Add(line);

            // Parse specific sections
            switch (currentSection)
            {
                case "General":
                    ParseGeneralLine(osuFile, trimmedLine);
                    break;
                case "Metadata":
                    ParseMetadataLine(osuFile, trimmedLine);
                    break;
                case "Difficulty":
                    ParseDifficultyLine(osuFile, trimmedLine);
                    break;
                case "TimingPoints":
                    ParseTimingPointLine(osuFile, trimmedLine);
                    break;
            }
        }

        // Save last section
        if (!string.IsNullOrEmpty(currentSection))
        {
            osuFile.RawSections[currentSection] = new List<string>(currentSectionLines);
        }

        return osuFile;
    }

    private void ParseGeneralLine(OsuFile osuFile, string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex < 0) return;

        var key = line.Substring(0, colonIndex).Trim();
        var value = line.Substring(colonIndex + 1).Trim();

        switch (key)
        {
            case "AudioFilename":
                osuFile.AudioFilename = value;
                break;
            case "AudioLeadIn":
                if (int.TryParse(value, out var leadIn))
                    osuFile.AudioLeadIn = leadIn;
                break;
            case "PreviewTime":
                if (int.TryParse(value, out var previewTime))
                    osuFile.PreviewTime = previewTime;
                break;
            case "Mode":
                if (int.TryParse(value, out var mode))
                    osuFile.Mode = mode;
                break;
        }
    }

    private void ParseMetadataLine(OsuFile osuFile, string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex < 0) return;

        var key = line.Substring(0, colonIndex).Trim();
        var value = line.Substring(colonIndex + 1).Trim();

        switch (key)
        {
            case "Title":
                osuFile.Title = value;
                break;
            case "TitleUnicode":
                osuFile.TitleUnicode = value;
                break;
            case "Artist":
                osuFile.Artist = value;
                break;
            case "ArtistUnicode":
                osuFile.ArtistUnicode = value;
                break;
            case "Creator":
                osuFile.Creator = value;
                break;
            case "Version":
                osuFile.Version = value;
                break;
            case "Source":
                osuFile.Source = value;
                break;
            case "Tags":
                osuFile.Tags = value;
                break;
            case "BeatmapID":
                if (int.TryParse(value, out var beatmapId))
                    osuFile.BeatmapID = beatmapId;
                break;
            case "BeatmapSetID":
                if (int.TryParse(value, out var beatmapSetId))
                    osuFile.BeatmapSetID = beatmapSetId;
                break;
        }
    }

    private void ParseDifficultyLine(OsuFile osuFile, string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex < 0) return;

        var key = line.Substring(0, colonIndex).Trim();
        var value = line.Substring(colonIndex + 1).Trim();

        switch (key)
        {
            case "HPDrainRate":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hp))
                    osuFile.HPDrainRate = hp;
                break;
            case "CircleSize":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cs))
                    osuFile.CircleSize = cs;
                break;
            case "OverallDifficulty":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var od))
                    osuFile.OverallDifficulty = od;
                break;
            case "ApproachRate":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ar))
                    osuFile.ApproachRate = ar;
                break;
            case "SliderMultiplier":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sm))
                    osuFile.SliderMultiplier = sm;
                break;
            case "SliderTickRate":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var str))
                    osuFile.SliderTickRate = str;
                break;
        }
    }

    private void ParseTimingPointLine(OsuFile osuFile, string line)
    {
        // Skip comments
        if (line.StartsWith("//")) return;

        try
        {
            var timingPoint = TimingPoint.Parse(line);
            osuFile.TimingPoints.Add(timingPoint);
        }
        catch
        {
            // Skip invalid timing point lines
        }
    }
}
