using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for reading hit error data directly from osu! memory.
/// This is used when on the results screen to get timing deviation data.
/// </summary>
public class HitErrorReaderService
{
    private readonly StructuredOsuMemoryReader _memoryReader;
    
    /// <summary>
    /// Lock object to prevent concurrent access to the memory reader.
    /// This is shared across all memory reading operations.
    /// </summary>
    public static readonly object MemoryReaderLock = new();
    
    public HitErrorReaderService()
    {
        _memoryReader = StructuredOsuMemoryReader.Instance;
    }
    
    /// <summary>
    /// Tries to read score identifiers from the results screen.
    /// Returns score, maxCombo, mods, and hit counts that can be used to identify the specific score.
    /// </summary>
    public ResultsScreenData? TryReadResultsScreenData()
    {
        if (!_memoryReader.CanRead)
        {
            Console.WriteLine("[HitErrorReader] Cannot read memory - not connected");
            return null;
        }
        
        lock (MemoryReaderLock)
        {
            try
            {
                // Try to read ResultsScreen structure
                var resultsScreen = new ResultsScreen();
                if (_memoryReader.TryRead(resultsScreen))
                {
                    Console.WriteLine($"[HitErrorReader] ResultsScreen read OK");
                    Console.WriteLine($"[HitErrorReader] ResultsScreen data:");
                    
                    // Log available properties
                    var type = resultsScreen.GetType();
                    foreach (var prop in type.GetProperties())
                    {
                        try
                        {
                            var value = prop.GetValue(resultsScreen);
                            Console.WriteLine($"[HitErrorReader]   {prop.Name}: {value}");
                        }
                        catch
                        {
                            Console.WriteLine($"[HitErrorReader]   {prop.Name}: <error reading>");
                        }
                    }
                    
                    // Return what we can read
                    return new ResultsScreenData
                    {
                        Score = resultsScreen.Score,
                        MaxCombo = resultsScreen.MaxCombo,
                        Mods = 0, // Mods type is complex, skip for now
                        Hit300 = resultsScreen.Hit300,
                        Hit100 = resultsScreen.Hit100,
                        Hit50 = resultsScreen.Hit50,
                        HitMiss = resultsScreen.HitMiss,
                        HitGeki = resultsScreen.HitGeki,
                        HitKatu = resultsScreen.HitKatu
                    };
                }
                else
                {
                    Console.WriteLine("[HitErrorReader] ResultsScreen read failed");
                }
                
                // Fallback: try Player structure (might have data after gameplay)
                var player = new Player();
                if (_memoryReader.TryRead(player))
                {
                    Console.WriteLine($"[HitErrorReader] Player fallback - Score: {player.Score}, Combo: {player.MaxCombo}");
                    return new ResultsScreenData
                    {
                        Score = player.Score,
                        MaxCombo = player.MaxCombo,
                        Mods = 0, // Mods type is complex, skip for now
                        Hit300 = player.Hit300,
                        Hit100 = player.Hit100,
                        Hit50 = player.Hit50,
                        HitMiss = player.HitMiss,
                        HitGeki = player.HitGeki,
                        HitKatu = player.HitKatu
                    };
                }
                
                Console.WriteLine("[HitErrorReader] Both ResultsScreen and Player read failed");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HitErrorReader] Error reading results screen: {ex.Message}");
                return null;
            }
        }
    }
    
    /// <summary>
    /// Reads hit error data from osu! memory while on the results screen.
    /// </summary>
    /// <returns>Analysis result with timing deviations, or null if reading failed.</returns>
    public TimingAnalysisResult? ReadHitErrorsFromMemory(string beatmapPath, float rate = 1.0f)
    {
        Console.WriteLine($"[HitErrorReader] CanRead: {_memoryReader.CanRead}");
        
        if (!_memoryReader.CanRead)
        {
            Console.WriteLine("[HitErrorReader] Cannot read osu! memory - reader not connected");
            return null;
        }
        
        Player player;
        bool readResult;
        
        try
        {
            lock (MemoryReaderLock)
            {
                // Read player data which contains hit errors
                player = new Player();
                readResult = _memoryReader.TryRead(player);
                Console.WriteLine($"[HitErrorReader] TryRead(Player) returned: {readResult}");
                
                if (!readResult)
                {
                    Console.WriteLine("[HitErrorReader] Failed to read Player data - trying alternative structures...");
                    
                    // Try reading GeneralData to see if memory reading works at all
                    var generalData = new GeneralData();
                    if (_memoryReader.TryRead(generalData))
                    {
                        Console.WriteLine($"[HitErrorReader] GeneralData read OK - Status: {generalData.RawStatus}, AudioTime: {generalData.AudioTime}");
                    }
                    else
                    {
                        Console.WriteLine("[HitErrorReader] GeneralData read also failed");
                    }
                    
                    return null;
                }
            }
            
            Console.WriteLine($"[HitErrorReader] Player data read - Accuracy: {player.Accuracy:F2}%");
            
            // Try to read hit errors array
            var hitErrors = player.HitErrors;
            
            if (hitErrors == null || hitErrors.Count == 0)
            {
                Console.WriteLine("[HitErrorReader] No hit errors available in memory");
                return null;
            }
            
            Console.WriteLine($"[HitErrorReader] Found {hitErrors.Count} hit errors in memory");
            
            // Create timing analysis result from hit errors
            var result = new TimingAnalysisResult
            {
                BeatmapPath = beatmapPath,
                Rate = rate,
                Success = true
            };
            
            // Hit errors are stored as offsets in milliseconds (negative = early, positive = late)
            // We don't have exact hit times, so we'll estimate based on index
            double estimatedTimePerHit = 1000.0; // Rough estimate, will be refined
            
            for (int i = 0; i < hitErrors.Count; i++)
            {
                var error = hitErrors[i];
                var deviation = new TimingDeviation(
                    i * estimatedTimePerHit, // Estimated expected time
                    i * estimatedTimePerHit + error, // Actual time
                    0, // Column unknown from memory
                    TimingDeviation.GetJudgementFromDeviation(Math.Abs(error))
                );
                result.Deviations.Add(deviation);
            }
            
            // Calculate statistics
            result.CalculateStatistics();
            
            Console.WriteLine($"[HitErrorReader] Analysis complete: UR={result.UnstableRate:F2}, Mean={result.MeanDeviation:F2}ms");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HitErrorReader] Error reading hit errors: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Reads hit error data and creates a proper timeline using beatmap note times.
    /// </summary>
    public TimingAnalysisResult? ReadHitErrorsWithBeatmap(string beatmapPath, OsuFileParser fileParser, float rate = 1.0f)
    {
        Console.WriteLine($"[HitErrorReader] ReadHitErrorsWithBeatmap - CanRead: {_memoryReader.CanRead}");
        
        if (!_memoryReader.CanRead)
        {
            Console.WriteLine("[HitErrorReader] Cannot read osu! memory - reader not connected");
            return null;
        }
        
        // Use lock to prevent concurrent access with SessionTrackerService
        Player player;
        List<int>? hitErrors;
        
        lock (MemoryReaderLock)
        {
            try
            {
                // First check if we can read general data (basic connectivity test)
                var generalData = new GeneralData();
                if (_memoryReader.TryRead(generalData))
                {
                    Console.WriteLine($"[HitErrorReader] GeneralData OK - Status: {generalData.RawStatus}, Mods: {generalData.Mods}");
                }
                else
                {
                    Console.WriteLine("[HitErrorReader] GeneralData read failed - memory reading not working");
                    return null;
                }
                
                // Read player data
                player = new Player();
                var playerReadResult = _memoryReader.TryRead(player);
                Console.WriteLine($"[HitErrorReader] TryRead(Player) returned: {playerReadResult}");
                
                if (!playerReadResult)
                {
                    Console.WriteLine("[HitErrorReader] Failed to read Player data");
                    return null;
                }
                
                Console.WriteLine($"[HitErrorReader] Player data - Accuracy: {player.Accuracy:F2}%, Score: {player.Score}");
                
                // Get hit errors while still in lock
                hitErrors = player.HitErrors?.ToList(); // Make a copy
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HitErrorReader] Error during memory read: {ex.Message}");
                return null;
            }
        }
        
        try
        {
            // Continue processing outside the lock
            
            if (hitErrors == null || hitErrors.Count == 0)
            {
                Console.WriteLine("[HitErrorReader] No hit errors in memory - trying alternative approach");
                
                // Log what data we can see
                Console.WriteLine($"[HitErrorReader] Player.Combo: {player.Combo}");
                Console.WriteLine($"[HitErrorReader] Player.MaxCombo: {player.MaxCombo}");
                Console.WriteLine($"[HitErrorReader] Player.Hit300: {player.Hit300}");
                Console.WriteLine($"[HitErrorReader] Player.Hit100: {player.Hit100}");
                Console.WriteLine($"[HitErrorReader] Player.Hit50: {player.Hit50}");
                Console.WriteLine($"[HitErrorReader] Player.HitMiss: {player.HitMiss}");
                
                return null;
            }
            
            Console.WriteLine($"[HitErrorReader] Found {hitErrors.Count} hit errors");
            
            // Parse beatmap to get actual note times
            var osuFile = fileParser.Parse(beatmapPath);
            int keyCount = (int)osuFile.CircleSize;
            double od = osuFile.OverallDifficulty;
            
            // Get hit object times from beatmap
            var hitObjectTimes = new List<double>();
            if (osuFile.RawSections.TryGetValue("HitObjects", out var hitObjectLines))
            {
                foreach (var line in hitObjectLines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;
                    
                    var hitObject = HitObject.Parse(line, keyCount);
                    if (hitObject != null)
                    {
                        // Adjust for rate
                        hitObjectTimes.Add(hitObject.Time / rate);
                        
                        // Add tail time for hold notes
                        if (hitObject.IsHold)
                        {
                            hitObjectTimes.Add(hitObject.EndTime / rate);
                        }
                    }
                }
            }
            
            hitObjectTimes.Sort();
            
            Console.WriteLine($"[HitErrorReader] Beatmap has {hitObjectTimes.Count} hit points, got {hitErrors.Count} errors");
            
            // Create result
            var result = new TimingAnalysisResult
            {
                BeatmapPath = beatmapPath,
                Rate = rate,
                Success = true,
                MapDuration = hitObjectTimes.Count > 0 ? hitObjectTimes.Max() : 0
            };
            
            // Match hit errors to note times
            int errorIndex = 0;
            foreach (var noteTime in hitObjectTimes)
            {
                if (errorIndex >= hitErrors.Count)
                    break;
                
                var error = hitErrors[errorIndex];
                var deviation = new TimingDeviation(
                    noteTime,
                    noteTime + error,
                    0, // Column unknown
                    TimingDeviation.GetJudgementFromDeviation(Math.Abs(error), od)
                );
                result.Deviations.Add(deviation);
                errorIndex++;
            }
            
            // Calculate statistics
            result.CalculateStatistics();
            
            Console.WriteLine($"[HitErrorReader] Analysis complete: {result.Deviations.Count} deviations, UR={result.UnstableRate:F2}, Mean={result.MeanDeviation:F2}ms");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HitErrorReader] Error: {ex.Message}");
            Console.WriteLine($"[HitErrorReader] Stack: {ex.StackTrace}");
            return null;
        }
    }
}

/// <summary>
/// Data read from the results screen that can be used to identify a specific score.
/// </summary>
public class ResultsScreenData
{
    public int Score { get; set; }
    public int MaxCombo { get; set; }
    public int Mods { get; set; }
    public int Hit300 { get; set; }
    public int Hit100 { get; set; }
    public int Hit50 { get; set; }
    public int HitMiss { get; set; }
    public int HitGeki { get; set; }
    public int HitKatu { get; set; }
    
    /// <summary>
    /// Checks if this results screen data matches a score from scores.db.
    /// </summary>
    public bool MatchesScore(OsuScore score)
    {
        // Match by score value, combo, and hit counts
        return score.TotalScore == Score 
            && score.MaxCombo == MaxCombo
            && score.Count300 == Hit300
            && score.Count100 == Hit100
            && score.Count50 == Hit50
            && score.CountMiss == HitMiss;
    }
    
    public override string ToString()
    {
        return $"Score={Score}, Combo={MaxCombo}, 300={Hit300}, 100={Hit100}, 50={Hit50}, Miss={HitMiss}";
    }
}

