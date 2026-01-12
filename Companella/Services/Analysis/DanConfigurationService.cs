using System.Text.Json;
using Companella.Models.Beatmap;
using Companella.Models.Difficulty;
using Companella.Models.Training;
using Companella.Services.Common;
using osu.Framework.Utils;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for managing dan configuration file.
/// Loads dans.json from %AppData%\Companella to preserve custom configurations across updates.
/// Uses ONNX model for classification when available, falls back to distance-based classification.
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
    private readonly DanModelService _modelService;

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

    /// <summary>
    /// Gets whether the ONNX model is loaded and available for classification.
    /// </summary>
    public bool IsModelLoaded => _modelService.IsLoaded;

    public DanConfigurationService()
    {
        // Use DataPaths for AppData-based storage
        _configPath = DataPaths.DansConfigFile;
        _modelService = new DanModelService();
    }

    /// <summary>
    /// Initializes the service by loading the configuration file and ONNX model.
    /// Call this on application startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadAsync();
        await _modelService.InitializeAsync();
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
    /// Classifies a map based on its MSD skillset scores, Interlude and Sunny difficulty ratings.
    /// Uses ONNX model inference when available, falls back to distance-based classification.
    /// </summary>
    /// <param name="msdScores">MSD skillset scores from MinaCalc.</param>
    /// <param name="interludeRating">Interlude (YAVSRG) difficulty rating.</param>
    /// <param name="sunnyRating">Sunny difficulty rating.</param>
    /// <returns>Classification result with dan level and variant.</returns>
    public DanClassificationResult ClassifyMap(SkillsetScores? msdScores, double interludeRating, double sunnyRating)
    {
        // Try ONNX model inference first
        if (_modelService.IsLoaded)
        {
            var modelResult = _modelService.ClassifyMap(msdScores, interludeRating, sunnyRating);
            if (modelResult != null)
            {
                return modelResult;
            }
        }

        // Fall back to distance-based classification
        throw new Exception("Model not loaded");
    }
    /// <summary>
    /// Classifies a map using OsuFile to calculate Interlude and Sunny difficulty.
    /// </summary>
    /// <param name="msdScores">MSD skillset scores from MinaCalc.</param>
    /// <param name="osuFile">The osu file to calculate difficulty from.</param>
    /// <param name="rate">Rate multiplier (1.0 = normal, 1.5 = DT, 0.75 = HT).</param>
    /// <returns>Classification result with dan level and variant.</returns>
    public DanClassificationResult ClassifyMap(SkillsetScores? msdScores, OsuFile osuFile, float rate = 1.0f)
    {
        double interludeRating = 0;
        double sunnyRating = 0;
        
        try
        {
            var interludeService = new InterludeDifficultyService();
            interludeRating = interludeService.CalculateDifficulty(osuFile, rate);
        }
        catch (Exception ex)
        {
            Logger.Info($"[DanConfig] Interlude calculation failed: {ex.Message}");
        }

        try
        {
            var sunnyService = new SunnyDifficultyService();
            sunnyRating = sunnyService.CalculateDifficulty(osuFile, rate);
        }
        catch (Exception ex)
        {
            Logger.Info($"[DanConfig] Sunny calculation failed: {ex.Message}");
        }

        return ClassifyMap(msdScores, interludeRating, sunnyRating);
    }

    /// <summary>
    /// Checks if a dan has valid data for classification.
    /// Returns true if at least Overall MSD or Interlude rating is > 0.
    /// </summary>
    private bool HasValidData(DanDefinition dan)
    {
        return dan.MsdValues.Overall > 0 || dan.InterludeRating > 0;
    }

    /// <summary>
    /// Calculates distance between input values and a dan definition.
    /// Uses Euclidean distance across all valid dimensions.
    /// </summary>
    private double CalculateDistance(MsdSkillsetValues msdInput, double interludeInput, DanDefinition dan)
    {
        double sumSquared = 0;
        int dimensions = 0;

        // MSD dimensions - only include if both input and dan have valid values
        if (msdInput.Overall > 0 && dan.MsdValues.Overall > 0)
        {
            var d = msdInput.Overall - dan.MsdValues.Overall;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Stream > 0 && dan.MsdValues.Stream > 0)
        {
            var d = msdInput.Stream - dan.MsdValues.Stream;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Jumpstream > 0 && dan.MsdValues.Jumpstream > 0)
        {
            var d = msdInput.Jumpstream - dan.MsdValues.Jumpstream;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Handstream > 0 && dan.MsdValues.Handstream > 0)
        {
            var d = msdInput.Handstream - dan.MsdValues.Handstream;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Stamina > 0 && dan.MsdValues.Stamina > 0)
        {
            var d = msdInput.Stamina - dan.MsdValues.Stamina;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Jackspeed > 0 && dan.MsdValues.Jackspeed > 0)
        {
            var d = msdInput.Jackspeed - dan.MsdValues.Jackspeed;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Chordjack > 0 && dan.MsdValues.Chordjack > 0)
        {
            var d = msdInput.Chordjack - dan.MsdValues.Chordjack;
            sumSquared += d * d;
            dimensions++;
        }
        if (msdInput.Technical > 0 && dan.MsdValues.Technical > 0)
        {
            var d = msdInput.Technical - dan.MsdValues.Technical;
            sumSquared += d * d;
            dimensions++;
        }

        // Interlude dimension
        if (interludeInput > 0 && dan.InterludeRating > 0)
        {
            var d = interludeInput - dan.InterludeRating;
            sumSquared += d * d;
            dimensions++;
        }

        if (dimensions == 0)
            return double.MaxValue;

        // Normalize by number of dimensions for fair comparison
        return Math.Sqrt(sumSquared / dimensions);
    }

    /// <summary>
    /// Determines the variant based on position relative to adjacent dans.
    /// Returns "--", "-", null, "+", or "++" for 5-tier classification.
    /// </summary>
    private string? DetermineVariant(int danIndex, MsdSkillsetValues msdInput, double interludeInput)
    {
        if (_configuration == null)
            return null;

        var currentDan = _configuration.Dans[danIndex];

        // Find previous valid dan
        DanDefinition? lowerDan = null;
        for (int i = danIndex - 1; i >= 0; i--)
        {
            if (HasValidData(_configuration.Dans[i]))
            {
                lowerDan = _configuration.Dans[i];
                break;
            }
        }

        // Find next valid dan
        DanDefinition? higherDan = null;
        for (int i = danIndex + 1; i < _configuration.Dans.Count; i++)
        {
            if (HasValidData(_configuration.Dans[i]))
            {
                higherDan = _configuration.Dans[i];
                break;
            }
        }

        // Use Overall MSD + Interlude for variant calculation (simplified 2D comparison)
        var inputValue = (msdInput.Overall > 0 ? msdInput.Overall : 0) + (interludeInput > 0 ? interludeInput : 0);
        var danValue = (currentDan.MsdValues.Overall > 0 ? currentDan.MsdValues.Overall : 0) + 
                       (currentDan.InterludeRating > 0 ? currentDan.InterludeRating : 0);

        double? lowerValue = null;
        if (lowerDan != null)
        {
            lowerValue = (lowerDan.MsdValues.Overall > 0 ? lowerDan.MsdValues.Overall : 0) + 
                         (lowerDan.InterludeRating > 0 ? lowerDan.InterludeRating : 0);
        }

        double? higherValue = null;
        if (higherDan != null)
        {
            higherValue = (higherDan.MsdValues.Overall > 0 ? higherDan.MsdValues.Overall : 0) + 
                          (higherDan.InterludeRating > 0 ? higherDan.InterludeRating : 0);
        }

        // Calculate boundaries for 5-tier system
        if (lowerValue.HasValue && higherValue.HasValue)
        {
            double totalRange = higherValue.Value - lowerValue.Value;
            double segmentSize = totalRange / 5.0;

            double boundary1 = lowerValue.Value + segmentSize;
            double boundary2 = lowerValue.Value + (segmentSize * 2);
            double boundary3 = lowerValue.Value + (segmentSize * 3);
            double boundary4 = lowerValue.Value + (segmentSize * 4);

            if (inputValue < boundary1)
                return "--";
            else if (inputValue < boundary2)
                return "-";
            else if (inputValue < boundary3)
                return null;
            else if (inputValue < boundary4)
                return "+";
            else
                return "++";
        }
        else if (lowerValue.HasValue)
        {
            double lowerRange = danValue - lowerValue.Value;
            if (lowerRange <= 0)
                return null;

            double segmentSize = lowerRange / 2.5;
            double boundary1 = lowerValue.Value + segmentSize;
            double boundary2 = lowerValue.Value + (segmentSize * 2);

            if (inputValue < boundary1)
                return "--";
            else if (inputValue < boundary2)
                return "-";
            else
                return null;
        }
        else if (higherValue.HasValue)
        {
            double upperRange = higherValue.Value - danValue;
            if (upperRange <= 0)
                return null;

            double segmentSize = upperRange / 2.5;
            double boundary1 = danValue + (segmentSize * 0.5);
            double boundary2 = danValue + segmentSize;

            if (inputValue > boundary2)
                return "++";
            else if (inputValue > boundary1)
                return "+";
            else
                return null;
        }

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
    /// Gets the MSD values for a specific dan.
    /// </summary>
    public MsdSkillsetValues? GetMsdValues(int danIndex)
    {
        var dan = GetDan(danIndex);
        return dan?.MsdValues;
    }

    /// <summary>
    /// Gets the Interlude rating for a specific dan.
    /// </summary>
    public double? GetInterludeRating(int danIndex)
    {
        var dan = GetDan(danIndex);
        return dan?.InterludeRating;
    }

    /// <summary>
    /// Gets the MSD values for a specific dan by label.
    /// </summary>
    public MsdSkillsetValues? GetMsdValues(string danLabel)
    {
        if (_configuration == null)
            return null;

        var dan = _configuration.Dans.FirstOrDefault(d => 
            d.Label.Equals(danLabel, StringComparison.OrdinalIgnoreCase));

        return dan?.MsdValues;
    }

    /// <summary>
    /// Gets the Interlude rating for a specific dan by label.
    /// </summary>
    public double? GetInterludeRating(string danLabel)
    {
        if (_configuration == null)
            return null;

        var dan = _configuration.Dans.FirstOrDefault(d => 
            d.Label.Equals(danLabel, StringComparison.OrdinalIgnoreCase));

        return dan?.InterludeRating;
    }
}
