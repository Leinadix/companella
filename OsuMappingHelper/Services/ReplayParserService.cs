using System.Text;
using OsuParsers.Decoders;
using OsuParsers.Replays;
using OsuParsers.Replays.Objects;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for parsing osu! replay (.osr) files.
/// </summary>
public class ReplayParserService
{
    private readonly OsuProcessDetector _processDetector;
    
    /// <summary>
    /// Creates a new ReplayParserService.
    /// </summary>
    public ReplayParserService(OsuProcessDetector processDetector)
    {
        _processDetector = processDetector;
    }
    
    /// <summary>
    /// Gets all possible replay folder paths.
    /// </summary>
    public List<string> GetReplayFolders()
    {
        var folders = new List<string>();
        var osuDir = _processDetector.GetOsuDirectory();
        
        if (string.IsNullOrEmpty(osuDir))
        {
            Console.WriteLine("[ReplayParser] osu! directory not found");
            return folders;
        }
        
        // Check Data/r folder (temporary replays)
        var dataR = Path.Combine(osuDir, "Data", "r");
        if (Directory.Exists(dataR))
        {
            folders.Add(dataR);
            Console.WriteLine($"[ReplayParser] Found Data/r folder: {dataR}");
        }
        
        // Check Replays folder (saved replays)
        var replays = Path.Combine(osuDir, "Replays");
        if (Directory.Exists(replays))
        {
            folders.Add(replays);
            Console.WriteLine($"[ReplayParser] Found Replays folder: {replays}");
        }
        
        // Check Data/r subfolder variations
        var dataReplay = Path.Combine(osuDir, "Data", "replayCache");
        if (Directory.Exists(dataReplay))
        {
            folders.Add(dataReplay);
            Console.WriteLine($"[ReplayParser] Found replayCache folder: {dataReplay}");
        }
        
        if (folders.Count == 0)
        {
            Console.WriteLine($"[ReplayParser] No replay folders found in: {osuDir}");
        }
        
        return folders;
    }
    
    /// <summary>
    /// Finds the most recently modified replay file across all replay folders.
    /// </summary>
    /// <param name="maxAgeSeconds">Maximum age of the replay file in seconds.</param>
    /// <returns>Path to the most recent replay file, or null if none found.</returns>
    public string? FindMostRecentReplay(int maxAgeSeconds = 60)
    {
        var folders = GetReplayFolders();
        if (folders.Count == 0)
            return null;
        
        try
        {
            var allReplayFiles = new List<FileInfo>();
            
            foreach (var folder in folders)
            {
                try
                {
                    var files = Directory.GetFiles(folder, "*.osr")
                        .Select(f => new FileInfo(f))
                        .ToList();
                    
                    Console.WriteLine($"[ReplayParser] Found {files.Count} .osr files in {Path.GetFileName(folder)}");
                    allReplayFiles.AddRange(files);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ReplayParser] Error scanning {folder}: {ex.Message}");
                }
            }
            
            if (allReplayFiles.Count == 0)
            {
                Console.WriteLine("[ReplayParser] No .osr files found in any folder");
                return null;
            }
            
            // Use the most recent time (either creation or last write)
            var recentFiles = allReplayFiles
                .Select(f => new { File = f, MostRecentTime = f.LastWriteTime > f.CreationTime ? f.LastWriteTime : f.CreationTime })
                .Where(f => (DateTime.Now - f.MostRecentTime).TotalSeconds <= maxAgeSeconds)
                .OrderByDescending(f => f.MostRecentTime)
                .ToList();
            
            if (recentFiles.Count == 0)
            {
                // Log the most recent file even if it's too old
                var newest = allReplayFiles.OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (newest != null)
                {
                    Console.WriteLine($"[ReplayParser] No recent replay files (max age: {maxAgeSeconds}s). Newest file is {(DateTime.Now - newest.LastWriteTime).TotalSeconds:F1}s old: {newest.Name}");
                }
                else
                {
                    Console.WriteLine($"[ReplayParser] No recent replay files found (max age: {maxAgeSeconds}s)");
                }
                return null;
            }
            
            var mostRecent = recentFiles[0];
            Console.WriteLine($"[ReplayParser] Found recent replay: {mostRecent.File.Name} ({(DateTime.Now - mostRecent.MostRecentTime).TotalSeconds:F1}s ago) in {mostRecent.File.DirectoryName}");
            return mostRecent.File.FullName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReplayParser] Error finding recent replay: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Parses a replay file and returns the replay data.
    /// </summary>
    /// <param name="replayPath">Path to the .osr file.</param>
    /// <returns>Parsed replay data, or null if parsing failed.</returns>
    public Replay? ParseReplay(string replayPath)
    {
        if (!File.Exists(replayPath))
        {
            Console.WriteLine($"[ReplayParser] Replay file not found: {replayPath}");
            return null;
        }
        
        try
        {
            var replay = ReplayDecoder.Decode(replayPath);
            Console.WriteLine($"[ReplayParser] Parsed replay: {replay.PlayerName} on {replay.BeatmapMD5Hash}");
            Console.WriteLine($"[ReplayParser] Score: {replay.ReplayScore}, Mods: {replay.Mods}");
            Console.WriteLine($"[ReplayParser] Replay frames: {replay.ReplayFrames.Count}");
            return replay;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReplayParser] Error parsing replay: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Finds and parses the most recent replay file.
    /// </summary>
    /// <param name="maxAgeSeconds">Maximum age of the replay file in seconds.</param>
    /// <returns>Parsed replay data, or null if not found or parsing failed.</returns>
    public Replay? FindAndParseRecentReplay(int maxAgeSeconds = 60)
    {
        var replayPath = FindMostRecentReplay(maxAgeSeconds);
        if (replayPath == null)
            return null;
        
        return ParseReplay(replayPath);
    }
    
    /// <summary>
    /// Extracts key press events from replay frames for osu!mania.
    /// Based on Mania-Replay-Master's approach for accurate timing.
    /// </summary>
    /// <param name="replay">The parsed replay.</param>
    /// <param name="keyCount">Number of keys (4 for 4K, 7 for 7K, etc).</param>
    /// <returns>List of key press events.</returns>
    public List<ManiaKeyEvent> ExtractManiaKeyEvents(Replay replay, int keyCount = 4)
    {
        var events = new List<ManiaKeyEvent>();
        
        if (replay.ReplayFrames.Count == 0)
        {
            Console.WriteLine("[ReplayParser] No replay frames to extract");
            return events;
        }
        
        int previousKeys = 0;
        long currentTime = 0;
        
        // Process all frames except the last one (following Mania-Replay-Master approach)
        int frameCount = replay.ReplayFrames.Count;
        for (int i = 0; i < frameCount - 1; i++)
        {
            var frame = replay.ReplayFrames[i];
            
            // Skip seed frame (special marker with TimeDiff = -12345)
            // Note: We don't add its time to currentTime
            if (frame.TimeDiff == -12345)
            {
                continue;
            }
            
            // Accumulate time for ALL frames (including negative TimeDiff)
            // The time accumulation happens regardless of the frame's content
            currentTime += frame.TimeDiff;
            
            // In mania, the X coordinate stores the key state as a bitmask
            int currentKeys = (int)frame.X;
            
            // Find key state changes
            for (int col = 0; col < keyCount; col++)
            {
                int colMask = 1 << col;
                bool wasPressed = (previousKeys & colMask) != 0;
                bool isPressed = (currentKeys & colMask) != 0;
                
                if (isPressed && !wasPressed)
                {
                    // Key pressed
                    events.Add(new ManiaKeyEvent(currentTime, col, true));
                }
                else if (!isPressed && wasPressed)
                {
                    // Key released
                    events.Add(new ManiaKeyEvent(currentTime, col, false));
                }
            }
            
            previousKeys = currentKeys;
        }
        
        Console.WriteLine($"[ReplayParser] Extracted {events.Count} key events from {frameCount} frames");
        if (events.Count > 0)
        {
            var presses = events.Where(e => e.IsPress).ToList();
            if (presses.Count > 0)
            {
                Console.WriteLine($"[ReplayParser] Press count: {presses.Count}, First press at {presses.First().Time:F0}ms, last at {presses.Last().Time:F0}ms");
            }
        }
        return events;
    }
    
    /// <summary>
    /// Finds replay files that match a specific beatmap by MD5 hash.
    /// </summary>
    /// <param name="beatmapPath">Path to the .osu beatmap file.</param>
    /// <returns>List of matching replay files sorted by most recent first.</returns>
    public List<string> FindReplaysForBeatmap(string beatmapPath)
    {
        var matches = new List<(string Path, DateTime Time)>();
        
        if (!File.Exists(beatmapPath))
        {
            Console.WriteLine($"[ReplayParser] Beatmap file not found: {beatmapPath}");
            return new List<string>();
        }
        
        // Calculate MD5 hash of the beatmap file
        string beatmapHash;
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = File.OpenRead(beatmapPath);
            var hashBytes = md5.ComputeHash(stream);
            beatmapHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            Console.WriteLine($"[ReplayParser] Beatmap MD5: {beatmapHash}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReplayParser] Error calculating beatmap hash: {ex.Message}");
            return new List<string>();
        }
        
        var folders = GetReplayFolders();
        
        foreach (var folder in folders)
        {
            try
            {
                var replayFiles = Directory.GetFiles(folder, "*.osr");
                Console.WriteLine($"[ReplayParser] Scanning {replayFiles.Length} replays in {Path.GetFileName(folder)}...");
                
                foreach (var replayPath in replayFiles)
                {
                    try
                    {
                        var replay = ReplayDecoder.Decode(replayPath);
                        if (replay.BeatmapMD5Hash?.ToLowerInvariant() == beatmapHash)
                        {
                            var fileInfo = new FileInfo(replayPath);
                            matches.Add((replayPath, fileInfo.LastWriteTime));
                            Console.WriteLine($"[ReplayParser] Found matching replay: {Path.GetFileName(replayPath)} ({replay.PlayerName})");
                        }
                    }
                    catch
                    {
                        // Skip unparseable replay files
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReplayParser] Error scanning folder {folder}: {ex.Message}");
            }
        }
        
        // Sort by time (most recent first) and return paths
        var sorted = matches.OrderByDescending(m => m.Time).Select(m => m.Path).ToList();
        Console.WriteLine($"[ReplayParser] Found {sorted.Count} replays matching beatmap");
        return sorted;
    }
    
    /// <summary>
    /// Finds and parses the most recent replay for a specific beatmap.
    /// </summary>
    /// <param name="beatmapPath">Path to the .osu beatmap file.</param>
    /// <returns>Parsed replay data, or null if not found.</returns>
    public Replay? FindAndParseReplayForBeatmap(string beatmapPath)
    {
        var matchingReplays = FindReplaysForBeatmap(beatmapPath);
        
        if (matchingReplays.Count == 0)
        {
            Console.WriteLine("[ReplayParser] No replays found for this beatmap");
            return null;
        }
        
        // Parse the most recent matching replay
        return ParseReplay(matchingReplays[0]);
    }
    
    /// <summary>
    /// Finds a replay by matching score data from the results screen with scores.db.
    /// This allows us to find the exact replay the user clicked on.
    /// </summary>
    /// <param name="resultsData">Score data read from the results screen.</param>
    /// <param name="beatmapHash">MD5 hash of the beatmap.</param>
    /// <returns>Parsed replay, or null if not found.</returns>
    public Replay? FindReplayByScoreData(ResultsScreenData resultsData, string beatmapHash)
    {
        Console.WriteLine($"[ReplayParser] Looking for replay matching: {resultsData}");
        Console.WriteLine($"[ReplayParser] Beatmap hash: {beatmapHash}");
        
        var osuDir = _processDetector.GetOsuDirectory();
        if (string.IsNullOrEmpty(osuDir))
        {
            Console.WriteLine("[ReplayParser] osu! directory not found");
            return null;
        }
        
        // Read scores.db to find matching score
        var scoresPath = Path.Combine(osuDir, "scores.db");
        if (!File.Exists(scoresPath))
        {
            Console.WriteLine("[ReplayParser] scores.db not found");
            return null;
        }
        
        try
        {
            var matchingScore = FindScoreInDatabase(scoresPath, resultsData, beatmapHash);
            
            if (matchingScore == null)
            {
                Console.WriteLine("[ReplayParser] No matching score found in scores.db");
                return null;
            }
            
            Console.WriteLine($"[ReplayParser] Found matching score: {matchingScore.PlayerName}, Timestamp: {matchingScore.Timestamp}");
            
            // Find the replay file by constructed filename: {BeatmapHash}-{Timestamp}.osr
            return FindReplayByFilename(matchingScore);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReplayParser] Error finding replay by score data: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Finds a score in scores.db that matches the given results screen data.
    /// </summary>
    private ScoreDbEntry? FindScoreInDatabase(string scoresPath, ResultsScreenData resultsData, string beatmapHash)
    {
        try
        {
            using var stream = File.OpenRead(scoresPath);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            
            // Version (Int32)
            var version = reader.ReadInt32();
            
            // Number of beatmaps (Int32)
            var beatmapCount = reader.ReadInt32();
            
            for (int i = 0; i < beatmapCount; i++)
            {
                // Beatmap MD5 hash (String)
                var mapHash = ReadOsuString(reader);
                
                // Number of scores for this beatmap (Int32)
                var scoreCount = reader.ReadInt32();
                
                for (int j = 0; j < scoreCount; j++)
                {
                    var score = ReadScore(reader);
                    
                    // Check if this score matches our criteria
                    if (mapHash?.ToLowerInvariant() == beatmapHash.ToLowerInvariant() ||
                        score.BeatmapHash?.ToLowerInvariant() == beatmapHash.ToLowerInvariant())
                    {
                        // Match by score value, combo, and hit counts
                        if (score.TotalScore == resultsData.Score &&
                            score.MaxCombo == resultsData.MaxCombo &&
                            score.Count300 == resultsData.Hit300 &&
                            score.Count100 == resultsData.Hit100 &&
                            score.Count50 == resultsData.Hit50 &&
                            score.CountMiss == resultsData.HitMiss)
                        {
                            Console.WriteLine($"[ReplayParser] Score match found at beatmap {i}, score {j}");
                            return score;
                        }
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReplayParser] Error reading scores.db: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Finds and parses a replay file by constructing its filename from beatmap hash and timestamp.
    /// Replay files in Data/r are named: {BeatmapHash}-{Timestamp}.osr
    /// </summary>
    private Replay? FindReplayByFilename(ScoreDbEntry scoreEntry)
    {
        if (string.IsNullOrEmpty(scoreEntry.BeatmapHash))
        {
            Console.WriteLine("[ReplayParser] Score has no beatmap hash");
            return null;
        }
        
        // Construct the expected filename: {BeatmapHash}-{Timestamp}.osr
        var expectedFilename = $"{scoreEntry.BeatmapHash}-{scoreEntry.Timestamp}.osr";
        Console.WriteLine($"[ReplayParser] Looking for replay file: {expectedFilename}");
        
        var folders = GetReplayFolders();
        
        foreach (var folder in folders)
        {
            var fullPath = Path.Combine(folder, expectedFilename);
            if (File.Exists(fullPath))
            {
                Console.WriteLine($"[ReplayParser] Found replay file: {fullPath}");
                return ParseReplay(fullPath);
            }
        }
        
        // Also try with lowercase beatmap hash
        var expectedFilenameLower = $"{scoreEntry.BeatmapHash.ToLowerInvariant()}-{scoreEntry.Timestamp}.osr";
        if (expectedFilenameLower != expectedFilename)
        {
            Console.WriteLine($"[ReplayParser] Trying lowercase: {expectedFilenameLower}");
            foreach (var folder in folders)
            {
                var fullPath = Path.Combine(folder, expectedFilenameLower);
                if (File.Exists(fullPath))
                {
                    Console.WriteLine($"[ReplayParser] Found replay file: {fullPath}");
                    return ParseReplay(fullPath);
                }
            }
        }
        
        // Fallback: search all files for matching pattern and verify by score metadata
        Console.WriteLine($"[ReplayParser] Exact filename not found, searching for pattern match...");
        foreach (var folder in folders)
        {
            try
            {
                var pattern = $"{scoreEntry.BeatmapHash}*.osr";
                var matchingFiles = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
                
                if (matchingFiles.Length > 0)
                {
                    Console.WriteLine($"[ReplayParser] Found {matchingFiles.Length} files matching beatmap hash in {Path.GetFileName(folder)}");
                    foreach (var file in matchingFiles)
                    {
                        Console.WriteLine($"[ReplayParser]   - {Path.GetFileName(file)}");
                    }
                    
                    // Parse each replay and find the one with matching score metadata
                    foreach (var file in matchingFiles)
                    {
                        try
                        {
                            var replay = ReplayDecoder.Decode(file);
                            
                            // Check if score metadata matches exactly
                            bool scoreMatches = replay.ReplayScore == scoreEntry.TotalScore;
                            bool comboMatches = replay.Combo == scoreEntry.MaxCombo;
                            bool hitsMatch = replay.Count300 == scoreEntry.Count300 &&
                                           replay.Count100 == scoreEntry.Count100 &&
                                           replay.Count50 == scoreEntry.Count50 &&
                                           replay.CountMiss == scoreEntry.CountMiss;
                            
                            Console.WriteLine($"[ReplayParser] Checking {Path.GetFileName(file)}: Score={replay.ReplayScore} ({(scoreMatches ? "OK" : "X")}), Combo={replay.Combo} ({(comboMatches ? "OK" : "X")}), Hits={replay.Count300}/{replay.Count100}/{replay.Count50}/{replay.CountMiss} ({(hitsMatch ? "OK" : "X")})");
                            
                            if (scoreMatches && comboMatches && hitsMatch)
                            {
                                Console.WriteLine($"[ReplayParser] Found exact metadata match: {Path.GetFileName(file)}");
                                return replay;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ReplayParser] Error parsing {Path.GetFileName(file)}: {ex.Message}");
                        }
                    }
                    
                    Console.WriteLine($"[ReplayParser] No exact metadata match found among {matchingFiles.Length} candidates");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReplayParser] Error searching folder {folder}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"[ReplayParser] No replay file found for beatmap hash: {scoreEntry.BeatmapHash}");
        return null;
    }
    
    /// <summary>
    /// Reads a score entry from a binary reader.
    /// </summary>
    private ScoreDbEntry ReadScore(BinaryReader reader)
    {
        var score = new ScoreDbEntry();
        
        // Mode (Byte)
        score.Mode = reader.ReadByte();
        
        // Score version (Int32)
        score.ScoreVersion = reader.ReadInt32();
        
        // Beatmap MD5 (String)
        score.BeatmapHash = ReadOsuString(reader);
        
        // Player name (String)
        score.PlayerName = ReadOsuString(reader);
        
        // Replay MD5 (String)
        score.ReplayHash = ReadOsuString(reader);
        
        // Hit counts
        score.Count300 = reader.ReadInt16();
        score.Count100 = reader.ReadInt16();
        score.Count50 = reader.ReadInt16();
        score.CountGeki = reader.ReadInt16();
        score.CountKatu = reader.ReadInt16();
        score.CountMiss = reader.ReadInt16();
        
        // Score
        score.TotalScore = reader.ReadInt32();
        
        // Max combo
        score.MaxCombo = reader.ReadInt16();
        
        // Perfect
        score.Perfect = reader.ReadBoolean();
        
        // Mods
        score.Mods = reader.ReadInt32();
        
        // HP graph (String, usually empty)
        ReadOsuString(reader);
        
        // Timestamp (Int64 - Windows ticks)
        score.Timestamp = reader.ReadInt64();
        
        // Replay data length (Int32, always -1 in scores.db)
        reader.ReadInt32();
        
        // Online score ID (Int64)
        score.OnlineScoreId = reader.ReadInt64();
        
        // Additional info for target practice (if mods include it)
        if ((score.Mods & (1 << 23)) != 0) // Target Practice mod
        {
            reader.ReadDouble();
        }
        
        return score;
    }
    
    /// <summary>
    /// Reads an osu! string from a binary reader.
    /// </summary>
    private string? ReadOsuString(BinaryReader reader)
    {
        var indicator = reader.ReadByte();
        if (indicator == 0x00)
            return null;
        
        if (indicator != 0x0b)
            throw new InvalidDataException($"Invalid string indicator: {indicator:X2}");
        
        // Read ULEB128 length
        var length = ReadUleb128(reader);
        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }
    
    /// <summary>
    /// Reads an unsigned LEB128 encoded integer.
    /// </summary>
    private uint ReadUleb128(BinaryReader reader)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        
        do
        {
            b = reader.ReadByte();
            result |= (uint)(b & 0x7f) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);
        
        return result;
    }
    
    /// <summary>
    /// Calculates the MD5 hash of a beatmap file.
    /// </summary>
    public string? GetBeatmapHash(string beatmapPath)
    {
        if (!File.Exists(beatmapPath))
            return null;
        
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = File.OpenRead(beatmapPath);
            var hashBytes = md5.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Gets the rate multiplier from replay mods.
    /// </summary>
    /// <param name="replay">The parsed replay.</param>
    /// <returns>Rate multiplier (1.5 for DT/NC, 0.75 for HT, 1.0 otherwise).</returns>
    public float GetRateFromMods(Replay replay)
    {
        var mods = (int)replay.Mods;
        
        // Mod flags from osu!
        const int MOD_DOUBLE_TIME = 64;
        const int MOD_NIGHTCORE = 512;
        const int MOD_HALF_TIME = 256;
        
        if ((mods & MOD_NIGHTCORE) != 0 || (mods & MOD_DOUBLE_TIME) != 0)
        {
            return 1.5f;
        }
        
        if ((mods & MOD_HALF_TIME) != 0)
        {
            return 0.75f;
        }
        
        return 1.0f;
    }
    
    /// <summary>
    /// Checks if mirror mod is active in the replay.
    /// </summary>
    /// <param name="replay">The parsed replay.</param>
    /// <returns>True if mirror mod is active.</returns>
    public bool HasMirrorMod(Replay replay)
    {
        var mods = (int)replay.Mods;
        const int MOD_MIRROR = 1 << 30; // Mirror is bit 30
        return (mods & MOD_MIRROR) != 0;
    }
}

/// <summary>
/// Score entry from scores.db.
/// </summary>
public class ScoreDbEntry
{
    public byte Mode { get; set; }
    public int ScoreVersion { get; set; }
    public string? BeatmapHash { get; set; }
    public string? PlayerName { get; set; }
    public string? ReplayHash { get; set; }
    public short Count300 { get; set; }
    public short Count100 { get; set; }
    public short Count50 { get; set; }
    public short CountGeki { get; set; }
    public short CountKatu { get; set; }
    public short CountMiss { get; set; }
    public int TotalScore { get; set; }
    public short MaxCombo { get; set; }
    public bool Perfect { get; set; }
    public int Mods { get; set; }
    public long Timestamp { get; set; }
    public long OnlineScoreId { get; set; }
}

/// <summary>
/// Represents a key press or release event in osu!mania.
/// </summary>
public class ManiaKeyEvent
{
    /// <summary>
    /// The time of the event in milliseconds.
    /// </summary>
    public double Time { get; set; }
    
    /// <summary>
    /// The column/key index (0-based).
    /// </summary>
    public int Column { get; set; }
    
    /// <summary>
    /// Whether this is a key press (true) or release (false).
    /// </summary>
    public bool IsPress { get; set; }
    
    /// <summary>
    /// Whether this event has been matched to a hit object.
    /// </summary>
    public bool IsMatched { get; set; }
    
    public ManiaKeyEvent(double time, int column, bool isPress)
    {
        Time = time;
        Column = column;
        IsPress = isPress;
    }
    
    public override string ToString()
    {
        return $"[{Time:F0}ms] Col{Column} {(IsPress ? "Press" : "Release")}";
    }
}

