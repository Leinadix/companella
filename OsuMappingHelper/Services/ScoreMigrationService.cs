using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for migrating scores from Companella session copies to their original beatmaps.
/// This allows players to keep their practice session scores on the original maps.
/// </summary>
public class ScoreMigrationService
{
    private readonly OsuProcessDetector _processDetector;
    private readonly OsuFileParser _fileParser;
    private string? _osuExePath;

    public ScoreMigrationService(OsuProcessDetector processDetector, OsuFileParser fileParser)
    {
        _processDetector = processDetector;
        _fileParser = fileParser;
    }

    /// <summary>
    /// Checks if osu! is currently running.
    /// </summary>
    private bool IsOsuRunning()
    {
        var osuProcesses = Process.GetProcessesByName("osu!");
        if (osuProcesses.Length == 0)
        {
            osuProcesses = Process.GetProcessesByName("osu");
        }

        foreach (var proc in osuProcesses)
        {
            proc.Dispose();
        }

        return osuProcesses.Length > 0;
    }

    /// <summary>
    /// Force closes osu! if it's running and stores the exe path for restart.
    /// </summary>
    /// <returns>True if osu! was closed or wasn't running, false if close failed.</returns>
    private bool ForceCloseOsu()
    {
        // Get osu! directory before closing (while we can still detect it)
        var osuDir = _processDetector.GetOsuDirectory();
        if (!string.IsNullOrEmpty(osuDir))
        {
            _osuExePath = Path.Combine(osuDir, "osu!.exe");
        }

        var osuProcesses = Process.GetProcessesByName("osu!");
        if (osuProcesses.Length == 0)
        {
            osuProcesses = Process.GetProcessesByName("osu");
        }

        if (osuProcesses.Length == 0)
        {
            Logger.Info("[ScoreMigration] osu! is not running");
            return true;
        }

        foreach (var proc in osuProcesses)
        {
            try
            {
                Logger.Info($"[ScoreMigration] Closing osu! (PID: {proc.Id})...");
                proc.Kill();
                proc.WaitForExit(5000); // Wait up to 5 seconds
                Logger.Info("[ScoreMigration] osu! closed successfully");
            }
            catch (Exception ex)
            {
                Logger.Info($"[ScoreMigration] Failed to close osu!: {ex.Message}");
                return false;
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Wait a moment for file handles to be released
        Thread.Sleep(1000);
        return true;
    }

    /// <summary>
    /// Starts osu! using the stored exe path.
    /// </summary>
    /// <returns>True if osu! was started successfully.</returns>
    private bool StartOsu()
    {
        if (string.IsNullOrEmpty(_osuExePath) || !File.Exists(_osuExePath))
        {
            // Try to get path from detector
            var osuDir = _processDetector.GetOsuDirectory();
            if (!string.IsNullOrEmpty(osuDir))
            {
                _osuExePath = Path.Combine(osuDir, "osu!.exe");
            }
        }

        if (string.IsNullOrEmpty(_osuExePath) || !File.Exists(_osuExePath))
        {
            Logger.Info("[ScoreMigration] Cannot start osu!: exe path not found");
            return false;
        }

        try
        {
            Logger.Info("[ScoreMigration] Starting osu!...");
            Process.Start(new ProcessStartInfo
            {
                FileName = _osuExePath,
                WorkingDirectory = Path.GetDirectoryName(_osuExePath),
                UseShellExecute = true
            });
            Logger.Info("[ScoreMigration] osu! start initiated");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Info($"[ScoreMigration] Failed to start osu!: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Waits for osu! to start running.
    /// </summary>
    /// <param name="timeoutSeconds">Maximum time to wait.</param>
    /// <returns>True if osu! started within the timeout.</returns>
    private bool WaitForOsuToStart(int timeoutSeconds = 30)
    {
        Logger.Info("[ScoreMigration] Waiting for osu! to start...");
        var startTime = DateTime.Now;
        
        while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
        {
            if (IsOsuRunning())
            {
                Logger.Info("[ScoreMigration] osu! is now running");
                // Give it a moment to fully initialize
                Thread.Sleep(2000);
                return true;
            }
            Thread.Sleep(500);
        }

        Logger.Info("[ScoreMigration] Timeout waiting for osu! to start");
        return false;
    }

    /// <summary>
    /// Performs a full restart of osu! to flush scores.db to disk.
    /// Starts osu!, waits for it to initialize, then closes it.
    /// </summary>
    /// <returns>True if the restart cycle completed successfully.</returns>
    private bool RestartOsuToFlushScores()
    {
        Logger.Info("[ScoreMigration] Restarting osu! to flush scores.db...");

        // Start osu!
        if (!StartOsu())
        {
            return false;
        }

        // Wait for it to start
        if (!WaitForOsuToStart(30))
        {
            return false;
        }

        // Give osu! time to fully load and sync scores
        Logger.Info("[ScoreMigration] Waiting for osu! to sync scores...");
        Thread.Sleep(3000);

        // Now close it again
        if (!ForceCloseOsu())
        {
            return false;
        }

        Logger.Info("[ScoreMigration] osu! restart cycle complete, scores.db should be up to date");
        return true;
    }

    /// <summary>
    /// Result of a score migration operation.
    /// </summary>
    public class MigrationResult
    {
        public bool Success { get; set; }
        public int SessionMapsFound { get; set; }
        public int ScoresFound { get; set; }
        public int ScoresMigrated { get; set; }
        public int ScoresFailed { get; set; }
        public string? Error { get; set; }
        public List<string> MigratedMaps { get; set; } = new();
    }

    /// <summary>
    /// Result of a cleanup operation (migrate + delete).
    /// </summary>
    public class CleanupResult
    {
        public bool Success { get; set; }
        public MigrationResult? MigrationResult { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesFailed { get; set; }
        public string? Error { get; set; }
        public List<string> DeletedFiles { get; set; } = new();
        public List<string> FailedFiles { get; set; } = new();
    }

    /// <summary>
    /// Finds all session copy beatmaps and migrates their scores to the original maps.
    /// Flow: restart osu! to flush scores -> close osu! -> migrate -> start osu!
    /// </summary>
    /// <returns>Result of the migration operation.</returns>
    public MigrationResult MigrateSessionScores()
    {
        return MigrateSessionScoresInternal(autoRestart: true);
    }

    /// <summary>
    /// Internal migration implementation with restart control.
    /// Flow: restart osu! to flush scores -> close osu! -> migrate -> optionally start osu!
    /// </summary>
    private MigrationResult MigrateSessionScoresInternal(bool autoRestart)
    {
        var result = new MigrationResult();
        bool wasOsuRunning = IsOsuRunning();

        try
        {
            var osuDir = _processDetector.GetOsuDirectory();
            if (string.IsNullOrEmpty(osuDir))
            {
                result.Error = "Could not find osu! directory";
                return result;
            }

            var songsDir = Path.Combine(osuDir, "Songs");
            var scoresPath = Path.Combine(osuDir, "scores.db");

            if (!File.Exists(scoresPath))
            {
                result.Error = "scores.db not found";
                return result;
            }

            // Step 0: Restart osu! to flush scores.db, then close it
            // This ensures any in-memory scores are written to disk
            if (wasOsuRunning)
            {
                Logger.Info("[ScoreMigration] osu! is running, need to flush scores first...");
                
                // Close osu! first
                if (!ForceCloseOsu())
                {
                    result.Error = "Failed to close osu! - please close it manually";
                    return result;
                }

                // Restart osu! to flush scores, then close it again
                if (!RestartOsuToFlushScores())
                {
                    result.Error = "Failed to restart osu! to flush scores";
                    return result;
                }
            }
            else
            {
                // osu! not running, but we should still do a restart cycle to ensure scores.db is fresh
                Logger.Info("[ScoreMigration] Starting osu! restart cycle to ensure scores are flushed...");
                if (!RestartOsuToFlushScores())
                {
                    // If restart fails but osu! wasn't running, we can still try to proceed
                    Logger.Info("[ScoreMigration] Warning: Could not restart osu! to flush scores, proceeding anyway...");
                }
            }

            // Perform the core migration logic
            result = PerformMigrationCore(osuDir, songsDir, scoresPath);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Logger.Info($"[ScoreMigration] Error: {ex.Message}");
        }
        finally
        {
            // Start osu! if it was running before and auto-restart is enabled
            if (autoRestart && wasOsuRunning)
            {
                StartOsu();
            }
        }

        return result;
    }

    /// <summary>
    /// Migrates scores from session copies to originals, then deletes all session copy files.
    /// Flow: restart osu! to flush scores -> close osu! -> migrate -> delete -> start osu!
    /// </summary>
    /// <returns>Result of the cleanup operation.</returns>
    public CleanupResult CleanupSessionMaps()
    {
        var result = new CleanupResult();
        bool wasOsuRunning = IsOsuRunning();

        try
        {
            var osuDir = _processDetector.GetOsuDirectory();
            if (string.IsNullOrEmpty(osuDir))
            {
                result.Error = "Could not find osu! directory";
                return result;
            }

            var songsDir = Path.Combine(osuDir, "Songs");
            var scoresPath = Path.Combine(osuDir, "scores.db");

            // Step 1: Find all session copy files first (before any osu! manipulation)
            Logger.Info("[ScoreMigration] Finding session copy files for cleanup...");
            var sessionFiles = FindAllSessionCopyFiles(songsDir);

            if (sessionFiles.Count == 0)
            {
                Logger.Info("[ScoreMigration] No session copy files found");
                result.Success = true;
                return result;
            }

            Logger.Info($"[ScoreMigration] Found {sessionFiles.Count} session copy files");

            // Step 2: Restart osu! to flush scores.db, then close it
            if (wasOsuRunning)
            {
                Logger.Info("[ScoreMigration] osu! is running, need to flush scores first...");
                
                // Close osu! first
                if (!ForceCloseOsu())
                {
                    result.Error = "Failed to close osu! - please close it manually";
                    return result;
                }

                // Restart osu! to flush scores, then close it again
                if (!RestartOsuToFlushScores())
                {
                    result.Error = "Failed to restart osu! to flush scores";
                    return result;
                }
            }
            else
            {
                // osu! not running, but we should still do a restart cycle to ensure scores.db is fresh
                Logger.Info("[ScoreMigration] Starting osu! restart cycle to ensure scores are flushed...");
                if (!RestartOsuToFlushScores())
                {
                    Logger.Info("[ScoreMigration] Warning: Could not restart osu! to flush scores, proceeding anyway...");
                }
            }

            // Step 3: Run migration (core logic only, no osu! lifecycle)
            if (File.Exists(scoresPath))
            {
                Logger.Info("[ScoreMigration] Running score migration before deletion...");
                result.MigrationResult = PerformMigrationCore(osuDir, songsDir, scoresPath);

                if (!result.MigrationResult.Success)
                {
                    result.Error = $"Migration failed: {result.MigrationResult.Error}";
                    return result;
                }
            }
            else
            {
                Logger.Info("[ScoreMigration] scores.db not found, skipping migration");
                result.MigrationResult = new MigrationResult { Success = true };
            }

            // Step 4: Delete all session copy files
            Logger.Info("[ScoreMigration] Deleting session copy files...");
            foreach (var filePath in sessionFiles)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        result.FilesDeleted++;
                        result.DeletedFiles.Add(filePath);
                        Logger.Info($"[ScoreMigration] Deleted: {Path.GetFileName(filePath)}");
                    }
                }
                catch (Exception ex)
                {
                    result.FilesFailed++;
                    result.FailedFiles.Add(filePath);
                    Logger.Info($"[ScoreMigration] Failed to delete {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            result.Success = true;
            Logger.Info($"[ScoreMigration] Cleanup complete: {result.FilesDeleted} files deleted, {result.FilesFailed} failed");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Logger.Info($"[ScoreMigration] Cleanup error: {ex.Message}");
        }
        finally
        {
            // Start osu! if it was running before
            if (wasOsuRunning)
            {
                StartOsu();
            }
        }

        return result;
    }

    /// <summary>
    /// Core migration logic without any osu! lifecycle management.
    /// Assumes osu! is already closed and scores.db is up to date.
    /// </summary>
    private MigrationResult PerformMigrationCore(string osuDir, string songsDir, string scoresPath)
    {
        var result = new MigrationResult();

        try
        {
            // Find all session copy beatmaps and their original counterparts
            Logger.Info("[ScoreMigration] Scanning for session copy beatmaps...");
            var sessionMaps = FindSessionCopyMaps(songsDir);
            result.SessionMapsFound = sessionMaps.Count;

            if (sessionMaps.Count == 0)
            {
                Logger.Info("[ScoreMigration] No session copy beatmaps found");
                result.Success = true;
                return result;
            }

            Logger.Info($"[ScoreMigration] Found {sessionMaps.Count} session copy beatmaps");

            // Build a mapping of session copy MD5 -> original MD5
            var hashMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sessionPath, originalPath) in sessionMaps)
            {
                var sessionHash = CalculateBeatmapHash(sessionPath);
                var originalHash = CalculateBeatmapHash(originalPath);

                if (!string.IsNullOrEmpty(sessionHash) && !string.IsNullOrEmpty(originalHash))
                {
                    hashMapping[sessionHash] = originalHash;
                    Logger.Info($"[ScoreMigration] Mapping: {Path.GetFileName(sessionPath)} -> {Path.GetFileName(originalPath)}");
                }
            }

            if (hashMapping.Count == 0)
            {
                Logger.Info("[ScoreMigration] No valid hash mappings created");
                result.Success = true;
                return result;
            }

            // Read scores.db and migrate scores
            Logger.Info("[ScoreMigration] Reading scores.db...");
            var (scores, version) = ReadScoresDb(scoresPath);

            var migratedScores = new List<OsuScore>();
            var scoresToRemove = new List<OsuScore>();

            foreach (var score in scores)
            {
                if (hashMapping.TryGetValue(score.BeatmapHash, out var originalHash))
                {
                    result.ScoresFound++;

                    // Create a copy with the original hash
                    var migratedScore = score.Clone();
                    migratedScore.BeatmapHash = originalHash;
                    migratedScores.Add(migratedScore);
                    scoresToRemove.Add(score);

                    result.ScoresMigrated++;

                    if (!result.MigratedMaps.Contains(score.BeatmapHash))
                    {
                        result.MigratedMaps.Add(score.BeatmapHash);
                    }
                }
            }

            if (result.ScoresMigrated == 0)
            {
                Logger.Info("[ScoreMigration] No scores found on session copies");
                result.Success = true;
                return result;
            }

            // Update scores list - remove old, add migrated
            foreach (var score in scoresToRemove)
            {
                scores.Remove(score);
            }
            scores.AddRange(migratedScores);

            // Write updated scores.db
            Logger.Info($"[ScoreMigration] Writing {result.ScoresMigrated} migrated scores...");
            WriteScoresDb(scoresPath, scores, version);

            result.Success = true;
            Logger.Info($"[ScoreMigration] Migration complete: {result.ScoresMigrated} scores migrated");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Logger.Info($"[ScoreMigration] Migration error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Finds all session copy .osu files in the Songs directory.
    /// </summary>
    private List<string> FindAllSessionCopyFiles(string songsDir)
    {
        var results = new List<string>();

        if (!Directory.Exists(songsDir))
            return results;

        var allOsuFiles = Directory.GetFiles(songsDir, "*.osu", SearchOption.AllDirectories);

        foreach (var osuPath in allOsuFiles)
        {
            try
            {
                var osuFile = _fileParser.Parse(osuPath);

                if (osuFile.Tags.Contains(BeatmapIndexer.SessionTag, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(osuPath);
                }
            }
            catch
            {
                // Skip files that can't be parsed
            }
        }

        return results;
    }

    /// <summary>
    /// Finds all session copy beatmaps and their corresponding original maps.
    /// </summary>
    private List<(string SessionPath, string OriginalPath)> FindSessionCopyMaps(string songsDir)
    {
        var results = new List<(string, string)>();

        if (!Directory.Exists(songsDir))
            return results;

        // Find all .osu files with the session tag
        var allOsuFiles = Directory.GetFiles(songsDir, "*.osu", SearchOption.AllDirectories);

        foreach (var osuPath in allOsuFiles)
        {
            try
            {
                var osuFile = _fileParser.Parse(osuPath);
                
                // Check if this is a session copy
                if (!osuFile.Tags.Contains(BeatmapIndexer.SessionTag, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Find the original map
                var originalPath = FindOriginalMap(osuPath, osuFile);
                if (!string.IsNullOrEmpty(originalPath))
                {
                    results.Add((osuPath, originalPath));
                }
            }
            catch
            {
                // Skip files that can't be parsed
            }
        }

        return results;
    }

    /// <summary>
    /// Finds the original map for a session copy.
    /// </summary>
    private string? FindOriginalMap(string sessionPath, Models.OsuFile sessionFile)
    {
        var directory = Path.GetDirectoryName(sessionPath);
        if (string.IsNullOrEmpty(directory))
            return null;

        // Session copies have version like "#001 OriginalVersion"
        // We need to find the map with just "OriginalVersion"
        var versionMatch = Regex.Match(sessionFile.Version, @"^#\d{3}\s+(.+)$");
        if (!versionMatch.Success)
            return null;

        var originalVersion = versionMatch.Groups[1].Value;

        // Look for the original map in the same directory
        var osuFiles = Directory.GetFiles(directory, "*.osu");
        foreach (var osuPath in osuFiles)
        {
            if (osuPath == sessionPath)
                continue;

            try
            {
                var osuFile = _fileParser.Parse(osuPath);
                
                // Skip other session copies
                if (osuFile.Tags.Contains(BeatmapIndexer.SessionTag, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if this is the original (same artist, title, creator, and original version)
                if (osuFile.Artist == sessionFile.Artist &&
                    osuFile.Title == sessionFile.Title &&
                    osuFile.Creator == sessionFile.Creator &&
                    osuFile.Version == originalVersion)
                {
                    return osuPath;
                }
            }
            catch
            {
                // Skip files that can't be parsed
            }
        }

        return null;
    }

    /// <summary>
    /// Calculates the MD5 hash of a beatmap file.
    /// </summary>
    private string CalculateBeatmapHash(string beatmapPath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(beatmapPath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Reads all scores from scores.db.
    /// </summary>
    private (List<OsuScore> Scores, int Version) ReadScoresDb(string scoresPath)
    {
        var scores = new List<OsuScore>();
        int version = 0;

        // Create backup first
        var backupPath = scoresPath + ".companella.bak";
        File.Copy(scoresPath, backupPath, true);
        Logger.Info($"[ScoreMigration] Created backup: {backupPath}");

        using var stream = File.OpenRead(scoresPath);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        // Version (Int32)
        version = reader.ReadInt32();

        // Number of beatmaps (Int32)
        var beatmapCount = reader.ReadInt32();

        for (int i = 0; i < beatmapCount; i++)
        {
            // Beatmap MD5 hash (String)
            var beatmapHash = ReadOsuString(reader);

            // Number of scores for this beatmap (Int32)
            var scoreCount = reader.ReadInt32();

            for (int j = 0; j < scoreCount; j++)
            {
                var score = new OsuScore
                {
                    BeatmapHash = beatmapHash
                };

                // Mode (Byte)
                score.Mode = reader.ReadByte();

                // Score version (Int32)
                score.ScoreVersion = reader.ReadInt32();

                // Beatmap MD5 again (String)
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
                score.HpGraph = ReadOsuString(reader);

                // Timestamp (Int64 - Windows ticks)
                score.Timestamp = reader.ReadInt64();

                // Replay data length (Int32, always -1 in scores.db)
                score.ReplayDataLength = reader.ReadInt32();

                // Online score ID (Int64)
                score.OnlineScoreId = reader.ReadInt64();

                // Additional info for target practice (if mods include it)
                if ((score.Mods & (1 << 23)) != 0) // Target Practice mod
                {
                    score.TargetPracticeAccuracy = reader.ReadDouble();
                }

                scores.Add(score);
            }
        }

        Logger.Info($"[ScoreMigration] Read {scores.Count} scores from scores.db");
        return (scores, version);
    }

    /// <summary>
    /// Writes all scores to scores.db.
    /// </summary>
    private void WriteScoresDb(string scoresPath, List<OsuScore> scores, int version)
    {
        // Group scores by beatmap hash
        var scoresByBeatmap = scores.GroupBy(s => s.BeatmapHash)
            .ToDictionary(g => g.Key, g => g.ToList());

        using var stream = File.Create(scoresPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        // Version (Int32)
        writer.Write(version);

        // Number of beatmaps (Int32)
        writer.Write(scoresByBeatmap.Count);

        foreach (var kvp in scoresByBeatmap)
        {
            var beatmapHash = kvp.Key;
            var beatmapScores = kvp.Value;

            // Beatmap MD5 hash (String)
            WriteOsuString(writer, beatmapHash);

            // Number of scores (Int32)
            writer.Write(beatmapScores.Count);

            foreach (var score in beatmapScores)
            {
                // Mode (Byte)
                writer.Write(score.Mode);

                // Score version (Int32)
                writer.Write(score.ScoreVersion);

                // Beatmap MD5 (String)
                WriteOsuString(writer, score.BeatmapHash);

                // Player name (String)
                WriteOsuString(writer, score.PlayerName);

                // Replay MD5 (String)
                WriteOsuString(writer, score.ReplayHash);

                // Hit counts
                writer.Write(score.Count300);
                writer.Write(score.Count100);
                writer.Write(score.Count50);
                writer.Write(score.CountGeki);
                writer.Write(score.CountKatu);
                writer.Write(score.CountMiss);

                // Score
                writer.Write(score.TotalScore);

                // Max combo
                writer.Write(score.MaxCombo);

                // Perfect
                writer.Write(score.Perfect);

                // Mods
                writer.Write(score.Mods);

                // HP graph (String)
                WriteOsuString(writer, score.HpGraph);

                // Timestamp
                writer.Write(score.Timestamp);

                // Replay data length (-1)
                writer.Write(score.ReplayDataLength);

                // Online score ID
                writer.Write(score.OnlineScoreId);

                // Target practice accuracy
                if ((score.Mods & (1 << 23)) != 0)
                {
                    writer.Write(score.TargetPracticeAccuracy);
                }
            }
        }

        Logger.Info($"[ScoreMigration] Wrote {scores.Count} scores to scores.db");
    }

    #region osu! Binary Format Helpers

    private string ReadOsuString(BinaryReader reader)
    {
        var flag = reader.ReadByte();
        if (flag == 0x00)
        {
            return string.Empty;
        }

        if (flag != 0x0b)
        {
            throw new InvalidDataException($"Invalid string flag: {flag}");
        }

        var length = ReadULEB128(reader);
        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

    private void WriteOsuString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write((byte)0x00);
            return;
        }

        writer.Write((byte)0x0b);
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteULEB128(writer, (uint)bytes.Length);
        writer.Write(bytes);
    }

    private uint ReadULEB128(BinaryReader reader)
    {
        uint result = 0;
        int shift = 0;

        while (true)
        {
            byte b = reader.ReadByte();
            result |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                break;

            shift += 7;
        }

        return result;
    }

    private void WriteULEB128(BinaryWriter writer, uint value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;

            if (value != 0)
                b |= 0x80;

            writer.Write(b);
        }
        while (value != 0);
    }

    #endregion
}

/// <summary>
/// Represents a score entry in osu!'s scores.db.
/// </summary>
public class OsuScore
{
    public string BeatmapHash { get; set; } = string.Empty;
    public byte Mode { get; set; }
    public int ScoreVersion { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string ReplayHash { get; set; } = string.Empty;
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
    public string HpGraph { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public int ReplayDataLength { get; set; } = -1;
    public long OnlineScoreId { get; set; }
    public double TargetPracticeAccuracy { get; set; }

    public OsuScore Clone()
    {
        return new OsuScore
        {
            BeatmapHash = BeatmapHash,
            Mode = Mode,
            ScoreVersion = ScoreVersion,
            PlayerName = PlayerName,
            ReplayHash = ReplayHash,
            Count300 = Count300,
            Count100 = Count100,
            Count50 = Count50,
            CountGeki = CountGeki,
            CountKatu = CountKatu,
            CountMiss = CountMiss,
            TotalScore = TotalScore,
            MaxCombo = MaxCombo,
            Perfect = Perfect,
            Mods = Mods,
            HpGraph = HpGraph,
            Timestamp = Timestamp,
            ReplayDataLength = ReplayDataLength,
            OnlineScoreId = OnlineScoreId,
            TargetPracticeAccuracy = TargetPracticeAccuracy
        };
    }
}

