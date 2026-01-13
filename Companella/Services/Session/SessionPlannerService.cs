using Companella.Models.Database;
using Companella.Models.Session;
using Companella.Services.Beatmap;
using Companella.Services.Common;
using Companella.Services.Database;

namespace Companella.Services.Session;

/// <summary>
/// Service for generating structured practice session plans.
/// Supports both curve-based and legacy phase-based session generation.
/// </summary>
public class SessionPlannerService
{
    private readonly MapsDatabaseService _mapsDatabase;
    private readonly OsuCollectionService _collectionService;
    private readonly BeatmapIndexer _beatmapIndexer;

    /// <summary>
    /// Default map duration estimate in seconds.
    /// </summary>
    private const int DefaultMapDurationSeconds = 120;

    /// <summary>
    /// MSD tolerance when searching for maps.
    /// </summary>
    private const double MsdTolerance = 0.5;

    /// <summary>
    /// Event raised when generation progress changes.
    /// </summary>
    public event EventHandler<SessionPlanProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Creates a new SessionPlannerService.
    /// </summary>
    public SessionPlannerService(
        MapsDatabaseService mapsDatabase,
        OsuCollectionService collectionService,
        BeatmapIndexer beatmapIndexer)
    {
        _mapsDatabase = mapsDatabase;
        _collectionService = collectionService;
        _beatmapIndexer = beatmapIndexer;
    }

    /// <summary>
    /// Generates a session plan from an MSD curve configuration.
    /// Uses per-point skillsets defined in the curve.
    /// </summary>
    /// <param name="curveConfig">The curve configuration defining MSD over time.</param>
    /// <returns>The generated session plan, or null if generation failed.</returns>
    public async Task<SessionPlan?> GenerateFromCurveAsync(MsdCurveConfig curveConfig)
    {
        var plan = new SessionPlan
        {
            Mode = SessionPlanMode.Manual,
            FocusSkillset = null, // Uses per-point skillsets from curve
            GeneratedAt = DateTime.UtcNow,
            CurveConfig = curveConfig.Clone()
        };

        // Calculate difficulty range from curve
        plan.WarmupDifficulty = curveConfig.GetMsdAtTime(0);
        plan.PeakDifficulty = curveConfig.MaxMsd;
        plan.CooldownEndDifficulty = curveConfig.GetMsdAtTime(100);

        ReportProgress("Generating session from curve...", 0);

        // Generate items by sampling the curve
        var items = await GenerateItemsFromCurveAsync(curveConfig);

        if (items.Count == 0)
        {
            ReportProgress("Failed to find maps matching the curve", 0);
            return null;
        }

        plan.Items.AddRange(items);
        ReportProgress($"Found {items.Count} maps", 70);

        // Reindex all items
        ReindexPlanItems(plan);

        // Create indexed copies of beatmaps
        ReportProgress("Creating indexed beatmap copies...", 75);
        var success = await CreateIndexedCopiesAsync(plan);
        if (!success)
        {
            ReportProgress("Failed to create indexed copies", 75);
            return null;
        }

        // Create the collection
        ReportProgress("Creating collection...", 90);
        var indexedPaths = plan.Items.Select(i => i.IndexedPath).ToList();
        var collectionName = _collectionService.CreateSessionCollection(indexedPaths, plan.GeneratedAt);

        if (string.IsNullOrEmpty(collectionName))
        {
            ReportProgress("Failed to create collection", 90);
            return null;
        }

        plan.CollectionName = collectionName;
        ReportProgress($"Session plan complete: {plan.Summary}", 100);

        return plan;
    }

    /// <summary>
    /// Generates session items by sampling the MSD curve at regular intervals.
    /// Uses per-point skillset focus for each segment.
    /// </summary>
    private async Task<List<SessionPlanItem>> GenerateItemsFromCurveAsync(MsdCurveConfig curveConfig)
    {
        var items = new List<SessionPlanItem>();
        var usedPaths = new HashSet<string>();

        var totalDurationSeconds = curveConfig.TotalSessionMinutes * 60;
        var segmentDurationSeconds = 300; // 5 minutes per segment
        var segmentCount = Math.Max(1, totalDurationSeconds / segmentDurationSeconds);

        var currentDuration = 0;

        for (int segment = 0; segment < segmentCount && currentDuration < totalDurationSeconds; segment++)
        {
            // Calculate time percentage for this segment
            var timePercent = (segment / (double)segmentCount) * 100.0;
            var targetMsd = curveConfig.GetMsdAtTime(timePercent);

            // Get skillset for this time point (from the nearest previous control point)
            var skillsetForSegment = curveConfig.GetSkillsetAtTime(timePercent);

            var progressPercent = (int)((segment / (double)segmentCount) * 60);
            var skillsetInfo = skillsetForSegment != null ? $" [{skillsetForSegment}]" : "";
            ReportProgress($"Finding maps for segment {segment + 1}/{segmentCount} (MSD: {targetMsd:F1}{skillsetInfo})...", progressPercent);

            // Find maps for this segment with the per-point skillset
            var segmentMaps = FindMapsForSegment(
                targetMsd,
                MsdTolerance,
                skillsetForSegment,
                segmentDurationSeconds,
                usedPaths,
                DefaultMapDurationSeconds);

            foreach (var map in segmentMaps)
            {
                if (currentDuration >= totalDurationSeconds)
                    break;

                try
                {
                    if (string.IsNullOrEmpty(map.BeatmapPath) || !File.Exists(map.BeatmapPath))
                    {
                        Logger.Info($"[SessionPlanner] Skipping map with invalid path: {map.BeatmapPath}");
                        continue;
                    }

                    // Determine phase based on time position
                    var mapTimePercent = (currentDuration / (double)totalDurationSeconds) * 100.0;
                    var phase = DeterminePhaseFromCurve(curveConfig, mapTimePercent);

                    var item = new SessionPlanItem
                    {
                        Phase = phase,
                        OriginalPath = map.BeatmapPath,
                        TargetMsd = targetMsd,
                        ActualMsd = map.OverallMsd,
                        DisplayName = map.DisplayName ?? Path.GetFileNameWithoutExtension(map.BeatmapPath),
                        Skillset = map.DominantSkillset ?? "unknown",
                        EstimatedDurationSeconds = DefaultMapDurationSeconds
                    };

                    items.Add(item);
                    usedPaths.Add(map.BeatmapPath);
                    currentDuration += DefaultMapDurationSeconds;
                }
                catch (Exception ex)
                {
                    Logger.Info($"[SessionPlanner] Skipping map due to error: {ex.Message}");
                    continue;
                }
            }
        }

        return await Task.FromResult(items);
    }

    /// <summary>
    /// Determines the session phase based on curve characteristics at a given time.
    /// </summary>
    private SessionPhase DeterminePhaseFromCurve(MsdCurveConfig curveConfig, double timePercent)
    {
        // Simple heuristic: check if MSD is increasing, at peak, or decreasing
        var currentMsd = curveConfig.GetMsdAtTime(timePercent);
        var prevMsd = curveConfig.GetMsdAtTime(Math.Max(0, timePercent - 5));

        // If we're in the first 20% and difficulty is low, it's warmup
        if (timePercent < 20 && currentMsd <= curveConfig.MinMsd + 1)
            return SessionPhase.Warmup;

        // If we're in the last 25% and difficulty is decreasing, it's cooldown
        if (timePercent > 75 && currentMsd < prevMsd)
            return SessionPhase.Cooldown;

        // Otherwise it's ramp-up
        return SessionPhase.RampUp;
    }

    /// <summary>
    /// Generates a session plan from analysis data (legacy method).
    /// </summary>
    public async Task<SessionPlan?> GenerateFromAnalysisAsync(
        SkillsTrendResult trends,
        SessionPlanConfig? config = null)
    {
        config ??= new SessionPlanConfig();

        var consistencyLevel = trends.OverallSkillLevel;
        var pushLevel = consistencyLevel * 1.15;

        var weakest = trends.GetWeakestSkillsets(1).FirstOrDefault();
        var focusSkillset = weakest;

        return await GenerateSessionAsync(
            SessionPlanMode.Analysis,
            consistencyLevel,
            pushLevel,
            focusSkillset,
            config);
    }

    /// <summary>
    /// Generates a session plan from manual input (legacy method).
    /// </summary>
    public async Task<SessionPlan?> GenerateManualAsync(
        string? focusSkillset,
        double targetPeakDifficulty,
        SessionPlanConfig? config = null)
    {
        config ??= new SessionPlanConfig();

        var consistencyLevel = targetPeakDifficulty * 0.9;

        return await GenerateSessionAsync(
            SessionPlanMode.Manual,
            consistencyLevel,
            targetPeakDifficulty,
            focusSkillset,
            config);
    }

    /// <summary>
    /// Core session generation logic (legacy phase-based).
    /// </summary>
    private async Task<SessionPlan?> GenerateSessionAsync(
        SessionPlanMode mode,
        double consistencyLevel,
        double peakLevel,
        string? focusSkillset,
        SessionPlanConfig config)
    {
        var plan = new SessionPlan
        {
            Mode = mode,
            FocusSkillset = focusSkillset,
            GeneratedAt = DateTime.UtcNow,
            WarmupDifficulty = consistencyLevel * (1 - config.WarmupDifficultyReduction),
            PeakDifficulty = peakLevel,
            CooldownEndDifficulty = peakLevel * (1 - config.CooldownDifficultyReduction)
        };

        ReportProgress("Generating warmup phase...", 0);

        var warmupItems = await GeneratePhaseItemsAsync(
            SessionPhase.Warmup,
            plan.WarmupDifficulty,
            plan.WarmupDifficulty,
            config.WarmupMinutes * 60,
            focusSkillset,
            config);

        if (warmupItems.Count == 0)
        {
            ReportProgress("Failed to find warmup maps", 0);
            return null;
        }

        plan.Items.AddRange(warmupItems);
        ReportProgress($"Found {warmupItems.Count} warmup maps", 20);

        ReportProgress("Generating ramp-up phase...", 25);
        var rampUpItems = await GeneratePhaseItemsAsync(
            SessionPhase.RampUp,
            plan.WarmupDifficulty * 1.05,
            plan.PeakDifficulty,
            config.RampUpMinutes * 60,
            focusSkillset,
            config);

        if (rampUpItems.Count == 0)
        {
            ReportProgress("Failed to find ramp-up maps", 25);
            return null;
        }

        plan.Items.AddRange(rampUpItems);
        ReportProgress($"Found {rampUpItems.Count} ramp-up maps", 60);

        ReportProgress("Generating cooldown phase...", 65);
        var cooldownItems = await GeneratePhaseItemsAsync(
            SessionPhase.Cooldown,
            plan.PeakDifficulty,
            plan.CooldownEndDifficulty,
            config.CooldownMinutes * 60,
            focusSkillset,
            config);

        if (cooldownItems.Count == 0)
        {
            ReportProgress("Failed to find cooldown maps", 65);
            return null;
        }

        plan.Items.AddRange(cooldownItems);
        ReportProgress($"Found {cooldownItems.Count} cooldown maps", 80);

        ReindexPlanItems(plan);

        ReportProgress("Creating indexed beatmap copies...", 85);
        var success = await CreateIndexedCopiesAsync(plan);
        if (!success)
        {
            ReportProgress("Failed to create indexed copies", 85);
            return null;
        }

        ReportProgress("Creating collection...", 95);
        var indexedPaths = plan.Items.Select(i => i.IndexedPath).ToList();
        var collectionName = _collectionService.CreateSessionCollection(indexedPaths, plan.GeneratedAt);

        if (string.IsNullOrEmpty(collectionName))
        {
            ReportProgress("Failed to create collection", 95);
            return null;
        }

        plan.CollectionName = collectionName;
        ReportProgress($"Session plan complete: {plan.Summary}", 100);

        return plan;
    }

    /// <summary>
    /// Generates items for a single phase (legacy method).
    /// </summary>
    private async Task<List<SessionPlanItem>> GeneratePhaseItemsAsync(
        SessionPhase phase,
        double startDifficulty,
        double endDifficulty,
        int targetDurationSeconds,
        string? focusSkillset,
        SessionPlanConfig config)
    {
        var items = new List<SessionPlanItem>();
        var currentDuration = 0;
        var usedPaths = new HashSet<string>();

        var isIncreasing = phase == SessionPhase.RampUp;
        var isDecreasing = phase == SessionPhase.Cooldown;

        var segmentDuration = 300;
        var segmentCount = Math.Max(1, targetDurationSeconds / segmentDuration);

        for (int segment = 0; segment < segmentCount && currentDuration < targetDurationSeconds; segment++)
        {
            double targetMsd;
            if (phase == SessionPhase.Warmup)
            {
                targetMsd = startDifficulty;
            }
            else
            {
                var progress = (double)segment / Math.Max(1, segmentCount - 1);
                targetMsd = startDifficulty + (endDifficulty - startDifficulty) * progress;
            }

            var segmentMaps = FindMapsForSegment(
                targetMsd,
                config.MsdTolerance,
                focusSkillset,
                segmentDuration,
                usedPaths,
                config.DefaultMapDurationSeconds);

            foreach (var map in segmentMaps)
            {
                if (currentDuration >= targetDurationSeconds)
                    break;

                try
                {
                    if (string.IsNullOrEmpty(map.BeatmapPath) || !File.Exists(map.BeatmapPath))
                    {
                        Logger.Info($"[SessionPlanner] Skipping map with invalid path: {map.BeatmapPath}");
                        continue;
                    }

                    var item = new SessionPlanItem
                    {
                        Phase = phase,
                        OriginalPath = map.BeatmapPath,
                        TargetMsd = targetMsd,
                        ActualMsd = map.OverallMsd,
                        DisplayName = map.DisplayName ?? Path.GetFileNameWithoutExtension(map.BeatmapPath),
                        Skillset = map.DominantSkillset ?? "unknown",
                        EstimatedDurationSeconds = config.DefaultMapDurationSeconds
                    };

                    items.Add(item);
                    usedPaths.Add(map.BeatmapPath);
                    currentDuration += config.DefaultMapDurationSeconds;
                }
                catch (Exception ex)
                {
                    Logger.Info($"[SessionPlanner] Skipping map due to error: {ex.Message}");
                    continue;
                }
            }
        }

        if (isDecreasing)
        {
            items = items.OrderByDescending(i => i.ActualMsd).ToList();
        }
        else if (isIncreasing)
        {
            items = items.OrderBy(i => i.ActualMsd).ToList();
        }

        return await Task.FromResult(items);
    }

    /// <summary>
    /// Finds maps for a single segment.
    /// </summary>
    private List<IndexedMap> FindMapsForSegment(
        double targetMsd,
        double tolerance,
        string? skillset,
        int targetDurationSeconds,
        HashSet<string> excludedPaths,
        int defaultMapDuration)
    {
        try
        {
            var criteria = new MapSearchCriteria
            {
                MinMsd = (float)(targetMsd - tolerance),
                MaxMsd = (float)(targetMsd + tolerance),
                KeyCount = 4,
                Skillset = skillset,
                OrderBy = MapSearchOrderBy.Random,
                Limit = (targetDurationSeconds / defaultMapDuration) + 5
            };

            var maps = _mapsDatabase.SearchMaps(criteria);

            maps = maps.Where(m =>
                !excludedPaths.Contains(m.BeatmapPath) &&
                m.OverallMsd > 0 &&
                !string.IsNullOrEmpty(m.BeatmapPath)).ToList();

            return maps;
        }
        catch (Exception ex)
        {
            Logger.Info($"[SessionPlanner] Error searching maps: {ex.Message}");
            return new List<IndexedMap>();
        }
    }

    /// <summary>
    /// Reindexes all items in the plan sequentially.
    /// </summary>
    private void ReindexPlanItems(SessionPlan plan)
    {
        var index = 1;
        foreach (var item in plan.Items)
        {
            item.Index = index++;
        }
    }

    /// <summary>
    /// Creates indexed copies of all beatmaps in the plan.
    /// </summary>
    private async Task<bool> CreateIndexedCopiesAsync(SessionPlan plan)
    {
        return await Task.Run(() =>
        {
            var failedItems = new List<SessionPlanItem>();

            foreach (var item in plan.Items)
            {
                try
                {
                    var indexedPath = _beatmapIndexer.CreateIndexedCopy(item.OriginalPath, item.Index);
                    if (string.IsNullOrEmpty(indexedPath))
                    {
                        Logger.Info($"[SessionPlanner] Failed to create indexed copy for: {item.DisplayName}");
                        failedItems.Add(item);
                    }
                    else
                    {
                        item.IndexedPath = indexedPath;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info($"[SessionPlanner] Error creating indexed copy for {item.DisplayName}: {ex.Message}");
                    failedItems.Add(item);
                }
            }

            foreach (var failed in failedItems)
            {
                plan.Items.Remove(failed);
            }

            if (failedItems.Count > 0)
            {
                Logger.Info($"[SessionPlanner] Skipped {failedItems.Count} maps due to indexing errors");
                ReindexPlanItems(plan);
            }

            return plan.Items.Count > 0;
        });
    }

    /// <summary>
    /// Reports progress to listeners.
    /// </summary>
    private void ReportProgress(string status, int percentage)
    {
        ProgressChanged?.Invoke(this, new SessionPlanProgressEventArgs
        {
            Status = status,
            Percentage = percentage
        });
        Logger.Info($"[SessionPlanner] {percentage}%: {status}");
    }

    /// <summary>
    /// Gets available skillsets for the focus dropdown.
    /// </summary>
    public static List<string> GetAvailableSkillsets()
    {
        return new List<string>
        {
            "stream",
            "jumpstream",
            "handstream",
            "stamina",
            "jackspeed",
            "chordjack",
            "technical"
        };
    }
}

/// <summary>
/// Event args for session plan generation progress.
/// </summary>
public class SessionPlanProgressEventArgs : EventArgs
{
    /// <summary>
    /// Current status message.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int Percentage { get; set; }
}
