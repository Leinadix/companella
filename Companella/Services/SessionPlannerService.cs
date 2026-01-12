using Companella.Models.Database;
using Companella.Models.Session;

namespace Companella.Services;

/// <summary>
/// Service for generating structured practice session plans.
/// Creates warmup, ramp-up, and cooldown phases with appropriate map selections.
/// </summary>
public class SessionPlannerService
{
    private readonly MapsDatabaseService _mapsDatabase;
    private readonly OsuCollectionService _collectionService;
    private readonly BeatmapIndexer _beatmapIndexer;

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
    /// Generates a session plan from analysis data.
    /// </summary>
    /// <param name="trends">The skill trends from session analysis.</param>
    /// <param name="config">Optional configuration for the session.</param>
    /// <returns>The generated session plan, or null if generation failed.</returns>
    public async Task<SessionPlan?> GenerateFromAnalysisAsync(
        SkillsTrendResult trends,
        SessionPlanConfig? config = null)
    {
        config ??= new SessionPlanConfig();

        // Determine difficulty levels from analysis
        var consistencyLevel = trends.OverallSkillLevel;
        var pushLevel = consistencyLevel * 1.15; // 15% above current level

        // Determine focus skillset (weakest that has some data)
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
    /// Generates a session plan from manual input.
    /// </summary>
    /// <param name="focusSkillset">The skillset to focus on (null for general).</param>
    /// <param name="targetPeakDifficulty">The target peak MSD difficulty.</param>
    /// <param name="config">Optional configuration for the session.</param>
    /// <returns>The generated session plan, or null if generation failed.</returns>
    public async Task<SessionPlan?> GenerateManualAsync(
        string? focusSkillset,
        double targetPeakDifficulty,
        SessionPlanConfig? config = null)
    {
        config ??= new SessionPlanConfig();

        // For manual mode, consistency level is ~10% below peak
        var consistencyLevel = targetPeakDifficulty * 0.9;

        return await GenerateSessionAsync(
            SessionPlanMode.Manual,
            consistencyLevel,
            targetPeakDifficulty,
            focusSkillset,
            config);
    }

    /// <summary>
    /// Core session generation logic.
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

        // Generate warmup phase
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

        // Generate ramp-up phase
        ReportProgress("Generating ramp-up phase...", 25);
        var rampUpItems = await GeneratePhaseItemsAsync(
            SessionPhase.RampUp,
            plan.WarmupDifficulty * 1.05, // Start slightly above warmup
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

        // Generate cooldown phase
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

        // Reindex all items
        ReindexPlanItems(plan);

        // Create indexed copies of beatmaps
        ReportProgress("Creating indexed beatmap copies...", 85);
        var success = await CreateIndexedCopiesAsync(plan);
        if (!success)
        {
            ReportProgress("Failed to create indexed copies", 85);
            return null;
        }

        // Create the collection
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
    /// Generates items for a single phase.
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

        // For ramp-up, we increase difficulty; for cooldown, we decrease
        var isIncreasing = phase == SessionPhase.RampUp;
        var isDecreasing = phase == SessionPhase.Cooldown;

        // Calculate how many "segments" we need
        var segmentDuration = 300; // 5 minutes per segment
        var segmentCount = Math.Max(1, targetDurationSeconds / segmentDuration);

        for (int segment = 0; segment < segmentCount && currentDuration < targetDurationSeconds; segment++)
        {
            // Calculate target difficulty for this segment
            double targetMsd;
            if (phase == SessionPhase.Warmup)
            {
                targetMsd = startDifficulty; // Constant for warmup
            }
            else
            {
                // Linear interpolation between start and end
                var progress = (double)segment / Math.Max(1, segmentCount - 1);
                targetMsd = startDifficulty + (endDifficulty - startDifficulty) * progress;
            }

            // Find maps for this segment
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
                    // Skip maps with invalid data
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

        // Ensure items are in the right order for decreasing phases
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
    /// Finds maps for a single segment of a phase.
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
                Limit = (targetDurationSeconds / defaultMapDuration) + 5 // Get extra to allow filtering
            };

            var maps = _mapsDatabase.SearchMaps(criteria);

            // Filter out already used maps and maps with invalid MSD data
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
    /// Skips maps that fail to index instead of failing completely.
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

            // Remove failed items from the plan
            foreach (var failed in failedItems)
            {
                plan.Items.Remove(failed);
            }

            if (failedItems.Count > 0)
            {
                Logger.Info($"[SessionPlanner] Skipped {failedItems.Count} maps due to indexing errors");
                // Reindex remaining items
                ReindexPlanItems(plan);
            }

            // Return true if we have at least some maps
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

