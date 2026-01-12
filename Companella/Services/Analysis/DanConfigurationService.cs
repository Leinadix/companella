using System.Text.Json;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Models.Training;
using Companella.Services.Common;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for managing dan configuration file.
/// Loads dans.json from %AppData%\Companella to preserve custom configurations across updates.
/// Uses YAVSRG difficulty ratings for classification.
/// </summary>
public class DanConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private DanConfiguration? _configuration;
    private readonly string _configPath;
    private string? _loadError;

    /// <summary>
    /// Gets the loaded configuration.
    /// </summary>
    public DanConfiguration? Configuration => _configuration;

    /// <summary>
    /// Gets whether the configuration has been loaded successfully.
    /// </summary>
    public bool IsLoaded => _configuration != null && _configuration.Dans.Count > 0;

    /// <summary>
    /// Gets the error message if loading failed.
    /// </summary>
    public string? LoadError => _loadError;

    public DanConfigurationService()
    {
        // Use DataPaths for AppData-based storage
        _configPath = DataPaths.DansConfigFile;
    }

    /// <summary>
    /// Initializes the service by loading the configuration file.
    /// Call this on application startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// Loads the configuration from the file.
    /// </summary>
    public async Task LoadAsync()
    {
        _loadError = null;

        if (!File.Exists(_configPath))
        {
            _loadError = $"dans.json not found at {_configPath}";
            Logger.Info($"[DanConfig] {_loadError}");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            _configuration = JsonSerializer.Deserialize<DanConfiguration>(json, JsonOptions);

            if (_configuration == null || _configuration.Dans.Count == 0)
            {
                _loadError = "dans.json is empty or invalid";
                Logger.Info($"[DanConfig] {_loadError}");
            }
            else
            {
                Logger.Info($"[DanConfig] Loaded {_configuration.Dans.Count} dan definitions (v{_configuration.Version})");
            }
        }
        catch (Exception ex)
        {
            _loadError = $"Failed to load dans.json: {ex.Message}";
            Logger.Info($"[DanConfig] {_loadError}");
        }
    }

    /// <summary>
    /// Classifies a map based on its top pattern and YAVSRG difficulty rating.
    /// Calculates YAVSRG difficulty internally from the OsuFile.
    /// </summary>
    /// <param name="topPatterns">Top patterns from analysis (uses the first/dominant one).</param>
    /// <param name="osuFile">The osu file to calculate YAVSRG difficulty from.</param>
    /// <returns>Classification result with dan level and Low/High variant.</returns>
    public DanClassificationResult ClassifyMap(List<TopPattern> topPatterns, OsuFile osuFile)
    {
        var result = new DanClassificationResult();

        if (_configuration == null || _configuration.Dans.Count == 0 || topPatterns.Count == 0 || osuFile == null)
        {
            return result;
        }

        // Calculate YAVSRG difficulty
        double yavsrgRating;
        try
        {
            var difficultyService = new InterludeDifficultyService();
            yavsrgRating = difficultyService.CalculateDifficulty(osuFile, 1.0f);
            result.YavsrgRating = yavsrgRating;
        }
        catch (Exception ex)
        {
            Logger.Info($"[DanConfig] YAVSRG calculation failed: {ex.Message}");
            return result;
        }

        // Get the dominant pattern (highest percentage), excluding Jump and Quad
        // Jump and Quad are single-chord patterns that don't represent valid skills for dan classification
        var dominant = topPatterns.FirstOrDefault(p => p.Type != PatternType.Jump && p.Type != PatternType.Quad && p.Type != PatternType.Hand);
        if (dominant == null)
        {
            return result;
        }

        result.DominantPattern = dominant.ShortName;
        var patternTypeName = dominant.Type.ToString();

        // Build a list of (danIndex, rating) for this pattern type
        var danRatings = new List<(int Index, double Rating)>();
        
        for (int i = 0; i < _configuration.Dans.Count; i++)
        {
            var dan = _configuration.Dans[i];
            if (dan.Patterns.TryGetValue(patternTypeName, out var rating))
            {
                danRatings.Add((i, rating));
            }
        }

        if (danRatings.Count == 0)
        {
            // No ratings for this pattern type, cannot classify
            return result;
        }

        // Find the closest dan by rating
        int bestDanIndex = -1;
        double bestDistance = double.MaxValue;

        foreach (var (index, rating) in danRatings)
        {
            var distance = Math.Abs(rating - yavsrgRating);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestDanIndex = index;
            }
        }

        if (bestDanIndex < 0)
        {
            return result;
        }

        var matchedDan = _configuration.Dans[bestDanIndex];
        var matchedRating = matchedDan.Patterns[patternTypeName];

        result.Label = matchedDan.Label;
        result.DanIndex = bestDanIndex;
        result.TargetRating = matchedRating;

        // Determine Low/High variant by comparing to adjacent dans
        result.Variant = DetermineVariant(
            bestDanIndex, 
            patternTypeName, 
            yavsrgRating, 
            matchedRating);

        // Calculate confidence based on how close we are to the target rating
        // Closer to target = higher confidence
        var maxDeviation = 1.0; // Rating deviation for 0% confidence
        result.Confidence = Math.Max(0, Math.Min(1, 1.0 - (bestDistance / maxDeviation)));

        return result;
    }

    /// <summary>
    /// Determines the variant based on where the rating falls within the dan range.
    /// Returns "--", "-", null, "+", or "++" for 5-tier classification.
    /// </summary>
    private string? DetermineVariant(
        int danIndex, 
        string patternType, 
        double yavsrgRating, 
        double danRating)
    {
        if (_configuration == null)
            return null;

        // Get the rating for the previous dan (lower tier)
        double? lowerDanRating = null;
        for (int i = danIndex - 1; i >= 0; i--)
        {
            if (_configuration.Dans[i].Patterns.TryGetValue(patternType, out var rating))
            {
                lowerDanRating = rating;
                break;
            }
        }

        // Get the rating for the next dan (higher tier)
        double? higherDanRating = null;
        for (int i = danIndex + 1; i < _configuration.Dans.Count; i++)
        {
            if (_configuration.Dans[i].Patterns.TryGetValue(patternType, out var rating))
            {
                higherDanRating = rating;
                break;
            }
        }

        // Calculate boundaries for 5-tier system: --, -, (mid), +, ++
        // Divide the full range from lowerDanRating to higherDanRating into 5 equal segments
        
        if (lowerDanRating.HasValue && higherDanRating.HasValue)
        {
            // Full range: from lowerDanRating to higherDanRating
            double totalRange = higherDanRating.Value - lowerDanRating.Value;
            double segmentSize = totalRange / 5.0;
            
            // Calculate boundaries
            double boundary1 = lowerDanRating.Value + segmentSize;      // --/ boundary
            double boundary2 = lowerDanRating.Value + (segmentSize * 2); // -/mid boundary
            double boundary3 = lowerDanRating.Value + (segmentSize * 3); // mid/+ boundary
            double boundary4 = lowerDanRating.Value + (segmentSize * 4); // +/++ boundary
            
            if (yavsrgRating < boundary1)
                return "--"; // Very low (bottom 20%)
            else if (yavsrgRating < boundary2)
                return "-"; // Low (20-40%)
            else if (yavsrgRating < boundary3)
                return null; // Mid (40-60%)
            else if (yavsrgRating < boundary4)
                return "+"; // High (60-80%)
            else
                return "++"; // Very high (top 20%)
        }
        else if (lowerDanRating.HasValue)
        {
            // Only lower dan available - use lower range, divide into 5 parts
            double lowerRange = danRating - lowerDanRating.Value;
            double segmentSize = lowerRange / 5.0;
            
            double boundary1 = lowerDanRating.Value + segmentSize;
            double boundary2 = lowerDanRating.Value + (segmentSize * 2);
            double boundary3 = lowerDanRating.Value + (segmentSize * 3);
            double boundary4 = lowerDanRating.Value + (segmentSize * 4);
            
            if (yavsrgRating < boundary1)
                return "--";
            else if (yavsrgRating < boundary2)
                return "-";
            else if (yavsrgRating < boundary3)
                return null;
            else if (yavsrgRating < boundary4)
                return "+";
            else
                return "++";
        }
        else if (higherDanRating.HasValue)
        {
            // Only higher dan available - use upper range, divide into 5 parts
            double upperRange = higherDanRating.Value - danRating;
            double segmentSize = upperRange / 5.0;
            
            double boundary1 = danRating + segmentSize;
            double boundary2 = danRating + (segmentSize * 2);
            double boundary3 = danRating + (segmentSize * 3);
            double boundary4 = danRating + (segmentSize * 4);
            
            if (yavsrgRating < boundary1)
                return "--";
            else if (yavsrgRating < boundary2)
                return "-";
            else if (yavsrgRating < boundary3)
                return null;
            else if (yavsrgRating < boundary4)
                return "+";
            else
                return "++";
        }
        
        // No adjacent dans - no variant
        return null;
    }

    /// <summary>
    /// Gets the dan at a specific index.
    /// </summary>
    public DanDefinition? GetDan(int index)
    {
        if (_configuration == null || index < 0 || index >= _configuration.Dans.Count)
            return null;

        return _configuration.Dans[index];
    }

    /// <summary>
    /// Gets all dan labels.
    /// </summary>
    public IReadOnlyList<string> GetAllLabels()
    {
        if (_configuration == null)
            return Array.Empty<string>();

        return _configuration.Dans.Select(d => d.Label).ToList();
    }

    /// <summary>
    /// Gets the YAVSRG rating threshold for a specific pattern at a specific dan.
    /// </summary>
    public double? GetPatternRating(int danIndex, string patternType)
    {
        var dan = GetDan(danIndex);
        if (dan == null)
            return null;

        return dan.Patterns.TryGetValue(patternType, out var rating) ? rating : null;
    }

    /// <summary>
    /// Gets the YAVSRG rating threshold for a specific pattern at a specific dan by label.
    /// </summary>
    public double? GetPatternRating(string danLabel, string patternType)
    {
        if (_configuration == null)
            return null;

        var dan = _configuration.Dans.FirstOrDefault(d => 
            d.Label.Equals(danLabel, StringComparison.OrdinalIgnoreCase));
        
        if (dan == null)
            return null;

        return dan.Patterns.TryGetValue(patternType, out var rating) ? rating : null;
    }
}
