using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Companella.Models.Difficulty;
using Companella.Models.Training;
using Companella.Services.Common;

namespace Companella.Services.Analysis;

/// <summary>
/// Service for dan classification using ONNX model inference.
/// Loads a pre-trained GradientBoostingRegressor model exported from Python.
/// </summary>
public class DanModelService
{
    /// <summary>
    /// Feature order must match the Python training script exactly.
    /// </summary>
    private static readonly string[] FeatureOrder = {
        "overall", "stream", "jumpstream", "handstream",
        "stamina", "jackspeed", "chordjack", "technical"
    };

    private const string ModelFileName = "dan_model.onnx";
    private const int FeatureCount = 10; // 8 MSD + 1 Interlude + 1 Sunny
    private const float MinDan = 1.0f;
    private const float MaxDan = 20.0f;

    private InferenceSession? _session;
    private readonly string _modelPath;
    private string? _loadError;

    /// <summary>
    /// Gets whether the model has been loaded successfully.
    /// </summary>
    public bool IsLoaded => _session != null;

    /// <summary>
    /// Gets the error message if loading failed.
    /// </summary>
    public string? LoadError => _loadError;

    public DanModelService()
    {
        // Model is stored next to the executable
        _modelPath = Path.Combine(DataPaths.ApplicationFolder, ModelFileName);
    }

    /// <summary>
    /// Initializes the service by loading the ONNX model.
    /// Call this on application startup.
    /// </summary>
    public Task InitializeAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the ONNX model from disk.
    /// </summary>
    public void Load()
    {
        _loadError = null;
        _session?.Dispose();
        _session = null;

        if (!File.Exists(_modelPath))
        {
            _loadError = $"Model file not found: {_modelPath}";
            Logger.Info($"[DanModel] {_loadError}");
            return;
        }

        try
        {
            _session = new InferenceSession(_modelPath);
            Logger.Info($"[DanModel] Loaded ONNX model from {_modelPath}");
        }
        catch (Exception ex)
        {
            _loadError = $"Failed to load model: {ex.Message}";
            Logger.Info($"[DanModel] {_loadError}");
        }
    }

    /// <summary>
    /// Runs inference and returns the raw continuous value (e.g., 5.75).
    /// </summary>
    /// <param name="msd">MSD skillset values.</param>
    /// <param name="interludeRating">Interlude (YAVSRG) difficulty rating.</param>
    /// <param name="sunnyRating">Sunny difficulty rating.</param>
    /// <returns>Raw model output, or -1 if inference failed.</returns>
    public float PredictRaw(MsdSkillsetValues msd, double interludeRating, double sunnyRating)
    {
        if (_session == null)
            return -1;

        try
        {
            // Build feature array in the same order as Python training
            var features = new float[FeatureCount];
            features[0] = (float)msd.Overall;
            features[1] = (float)msd.Stream;
            features[2] = (float)msd.Jumpstream;
            features[3] = (float)msd.Handstream;
            features[4] = (float)msd.Stamina;
            features[5] = (float)msd.Jackspeed;
            features[6] = (float)msd.Chordjack;
            features[7] = (float)msd.Technical;
            features[8] = (float)interludeRating;
            features[9] = (float)sunnyRating;
            
            Logger.Info($"[DanModel] Input: Overall={features[0]:F2}, Stream={features[1]:F2}, JS={features[2]:F2}, HS={features[3]:F2}, Stam={features[4]:F2}, Jack={features[5]:F2}, CJ={features[6]:F2}, Tech={features[7]:F2}, Interlude={features[8]:F2}, Sunny={features[9]:F2}");

            // Create input tensor with shape [1, 10]
            var inputTensor = new DenseTensor<float>(features, new[] { 1, FeatureCount });

            // Get the input name from the model
            var inputName = _session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            // Run inference
            using var results = _session.Run(inputs);
            
            // Log what we got back
            var firstResult = results.First();
            Logger.Info($"[DanModel] Output name: {firstResult.Name}, Type: {firstResult.ValueType}");
            
            // Try to get the value - sklearn regressors output a 1D array
            float rawValue;
            if (firstResult.Value is float[] floatArray && floatArray.Length > 0)
            {
                rawValue = floatArray[0];
            }
            else if (firstResult.Value is double[] doubleArray && doubleArray.Length > 0)
            {
                rawValue = (float)doubleArray[0];
            }
            else
            {
                var tensor = firstResult.AsTensor<float>();
                rawValue = tensor.GetValue(0);
            }
            
            Logger.Info($"[DanModel] Raw output: {rawValue}");
            return Math.Clamp(rawValue, MinDan, MaxDan);
        }
        catch (Exception ex)
        {
            Logger.Info($"[DanModel] Inference failed: {ex.Message}\n{ex.StackTrace}");
            return -1;
        }
    }

    /// <summary>
    /// Runs inference using SkillsetScores.
    /// </summary>
    public float PredictRaw(SkillsetScores? msdScores, double interludeRating, double sunnyRating)
    {
        if (msdScores == null)
            return -1;

        var msd = MsdSkillsetValues.FromSkillsetScores(msdScores);
        return PredictRaw(msd, interludeRating, sunnyRating);
    }

    /// <summary>
    /// Parses a raw prediction value into dan index and variant.
    /// Uses rounding to determine dan level, then offset from center for variant.
    /// Example: 5.51 rounds to 6, offset is -0.49, so variant is "--" (low end of 6).
    /// </summary>
    /// <param name="rawValue">The continuous model output (1.0 - 20.0).</param>
    /// <returns>Dan index (0-19) and variant (--/-/null/+/++).</returns>
    public (int DanIndex, string? Variant) ParsePrediction(float rawValue)
    {
        if (rawValue < MinDan)
            return (0, "--");

        if (rawValue >= MaxDan)
            return (19, "++");

        // Round to get dan level (1-20), then convert to 0-based index
        int danLevel = (int)Math.Round(rawValue);
        danLevel = Math.Clamp(danLevel, 1, 20);
        int danIndex = danLevel - 1;

        // Calculate offset from center (-0.5 to +0.5)
        // Negative = below center (low end), Positive = above center (high end)
        float offset = rawValue - danLevel;

        // Map offset to variant
        string? variant = offset switch
        {
            <= -0.3f => "--",
            <= -0.1f => "-",
            < 0.1f => null,
            < 0.3f => "+",
            _ => "++"
        };

        return (danIndex, variant);
    }

    /// <summary>
    /// Classifies a map using ONNX model inference.
    /// </summary>
    /// <param name="msdScores">MSD skillset scores from MinaCalc.</param>
    /// <param name="interludeRating">Interlude (YAVSRG) difficulty rating.</param>
    /// <param name="sunnyRating">Sunny difficulty rating.</param>
    /// <returns>Classification result with dan level and variant, or null if inference failed.</returns>
    public DanClassificationResult? ClassifyMap(SkillsetScores? msdScores, double interludeRating, double sunnyRating)
    {
        if (!IsLoaded)
            return null;

        if (msdScores == null && interludeRating <= 0)
            return null;

        var msd = msdScores != null 
            ? MsdSkillsetValues.FromSkillsetScores(msdScores) 
            : new MsdSkillsetValues(0);

        var rawValue = PredictRaw(msd, interludeRating, sunnyRating);
        if (rawValue < 0)
        {
            Logger.Info("[DanModel] PredictRaw returned error (-1)");
            return null;
        }

        rawValue += 1;

        var (danIndex, variant) = ParsePrediction(rawValue);
        var danLabel = DanLookup.GetLabel(danIndex);

        // Confidence based on how close to center (offset = 0 means 100% confidence)
        int danLevel = (int)Math.Round(rawValue);
        float offset = Math.Abs(rawValue - danLevel);
        double confidence = Math.Max(0, 1.0 - (offset * 2)); // 0 offset = 1.0, 0.5 offset = 0.0

        return new DanClassificationResult
        {
            Label = danLabel ?? "?",
            Variant = variant,
            DanIndex = danIndex,
            MsdValues = msd,
            InterludeRating = interludeRating,
            DominantSkillset = msd.DominantSkillset,
            Confidence = confidence,
            RawModelOutput = rawValue
        };
    }

    /// <summary>
    /// Classifies a map using MsdSkillsetValues directly.
    /// </summary>
    public DanClassificationResult? ClassifyMap(MsdSkillsetValues msd, double interludeRating, double sunnyRating)
    {
        if (!IsLoaded)
            return null;

        var rawValue = PredictRaw(msd, interludeRating, sunnyRating);
        if (rawValue < 0)
            return null;

        var (danIndex, variant) = ParsePrediction(rawValue);
        var danLabel = DanLookup.GetLabel(danIndex);

        // Confidence based on how close to center (offset = 0 means 100% confidence)
        int danLevel = (int)Math.Round(rawValue);
        float offset = Math.Abs(rawValue - danLevel);
        double confidence = Math.Max(0, 1.0 - (offset * 2));

        return new DanClassificationResult
        {
            Label = danLabel ?? "?",
            Variant = variant,
            DanIndex = danIndex,
            MsdValues = msd,
            InterludeRating = interludeRating,
            DominantSkillset = msd.DominantSkillset,
            Confidence = confidence,
            RawModelOutput = rawValue
        };
    }

    /// <summary>
    /// Disposes the ONNX session.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
