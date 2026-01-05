using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for managing the maps database (maps.db).
/// Indexes beatmaps with MSD scores and player statistics.
/// Database is stored in %AppData%\Companella to preserve data across updates.
/// </summary>
public class MapsDatabaseService : IDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly MsdAnalyzer? _msdAnalyzer;
    private readonly OsuFileParser _fileParser;
    private BeatmapApiService? _beatmapApiService;
    private bool _isDisposed;

    private CancellationTokenSource? _indexingCts;
    private bool _isIndexing;

    // Cache for existing map metadata to avoid per-file database queries
    private ConcurrentDictionary<string, CachedMapInfo>? _mapInfoCache;

    // Batch write queue for efficient database inserts
    private readonly ConcurrentQueue<IndexedMap> _pendingWrites = new();
    private readonly object _batchWriteLock = new();
    private const int BatchWriteSize = 50;


    /// <summary>
    /// Event raised when indexing progress changes.
    /// </summary>
    public event EventHandler<IndexingProgressEventArgs>? IndexingProgressChanged;

    /// <summary>
    /// Event raised when indexing completes.
    /// </summary>
    public event EventHandler<IndexingCompletedEventArgs>? IndexingCompleted;

    /// <summary>
    /// Whether indexing is currently in progress.
    /// </summary>
    public bool IsIndexing => _isIndexing;

    /// <summary>
    /// Sets the BeatmapApiService for uploading maps to the server during indexing.
    /// </summary>
    public void SetBeatmapApiService(BeatmapApiService? apiService)
    {
        _beatmapApiService = apiService;
    }

    /// <summary>
    /// Creates a new MapsDatabaseService.
    /// </summary>
    public MapsDatabaseService()
    {
        // Use DataPaths for AppData-based storage
        _databasePath = DataPaths.MapsDatabase;
        _connectionString = $"Data Source={_databasePath}";
        _fileParser = new OsuFileParser();

        if (ToolPaths.MsdCalculatorExists)
        {
            _msdAnalyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
        }

        InitializeDatabase();
    }

    /// <summary>
    /// Initializes the database schema.
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var createMapsTable = @"
                CREATE TABLE IF NOT EXISTS Maps (
                    BeatmapPath TEXT PRIMARY KEY,
                    FileHash TEXT NOT NULL,
                    FileSize INTEGER DEFAULT 0,
                    LastModified INTEGER DEFAULT 0,
                    Title TEXT,
                    Artist TEXT,
                    Version TEXT,
                    Creator TEXT,
                    CircleSize REAL,
                    Mode INTEGER,
                    DominantSkillset TEXT,
                    OverallMsd REAL,
                    LastAnalyzed TEXT NOT NULL
                )";

            // Migration: add FileSize and LastModified columns if they don't exist
            var addFileSizeColumn = "ALTER TABLE Maps ADD COLUMN FileSize INTEGER DEFAULT 0";
            var addLastModifiedColumn = "ALTER TABLE Maps ADD COLUMN LastModified INTEGER DEFAULT 0";

            var createMsdScoresTable = @"
                CREATE TABLE IF NOT EXISTS MapMsdScores (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BeatmapPath TEXT NOT NULL,
                    Rate REAL NOT NULL,
                    Overall REAL,
                    Stream REAL,
                    Jumpstream REAL,
                    Handstream REAL,
                    Stamina REAL,
                    Jackspeed REAL,
                    Chordjack REAL,
                    Technical REAL,
                    FOREIGN KEY (BeatmapPath) REFERENCES Maps(BeatmapPath) ON DELETE CASCADE,
                    UNIQUE(BeatmapPath, Rate)
                )";

            var createPlayerStatsTable = @"
                CREATE TABLE IF NOT EXISTS MapPlayerStats (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BeatmapPath TEXT NOT NULL,
                    SessionPlayId INTEGER,
                    Accuracy REAL NOT NULL,
                    RecordedAt TEXT NOT NULL,
                    FOREIGN KEY (BeatmapPath) REFERENCES Maps(BeatmapPath) ON DELETE CASCADE
                )";

            using (var cmd = new SqliteCommand(createMapsTable, connection))
                cmd.ExecuteNonQuery();

            using (var cmd = new SqliteCommand(createMsdScoresTable, connection))
                cmd.ExecuteNonQuery();

            using (var cmd = new SqliteCommand(createPlayerStatsTable, connection))
                cmd.ExecuteNonQuery();

            // Run migrations for new columns (ignore errors if columns already exist)
            try
            {
                using var cmd = new SqliteCommand(addFileSizeColumn, connection);
                cmd.ExecuteNonQuery();
            }
            catch { /* Column already exists */ }

            try
            {
                using var cmd = new SqliteCommand(addLastModifiedColumn, connection);
                cmd.ExecuteNonQuery();
            }
            catch { /* Column already exists */ }

            // Create indexes for faster queries
            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_maps_overallmsd ON Maps(OverallMsd)",
                "CREATE INDEX IF NOT EXISTS idx_maps_dominantskillset ON Maps(DominantSkillset)",
                "CREATE INDEX IF NOT EXISTS idx_maps_mode ON Maps(Mode)",
                "CREATE INDEX IF NOT EXISTS idx_msdscores_beatmappath ON MapMsdScores(BeatmapPath)",
                "CREATE INDEX IF NOT EXISTS idx_playerstats_beatmappath ON MapPlayerStats(BeatmapPath)"
            };

            foreach (var index in indexes)
            {
                using var cmd = new SqliteCommand(index, connection);
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine($"[MapsDB] Database initialized at: {_databasePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error initializing database: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets the total number of indexed maps.
    /// </summary>
    public int GetMapCount()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Maps", connection);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error getting map count: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Gets the number of 4K mania maps indexed.
    /// </summary>
    public int Get4KMapCount()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Maps WHERE Mode = 3 AND CircleSize = 4", connection);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error getting 4K map count: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Indexes a single beatmap.
    /// </summary>
    public async Task<bool> IndexBeatmapAsync(string beatmapPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(beatmapPath))
        {
            Console.WriteLine($"[MapsDB] File not found: {beatmapPath}");
            return false;
        }

        try
        {
            // Check if map is already indexed and hasn't changed
            var fileHash = ComputeFileHash(beatmapPath);
            var existingMap = GetMapByPath(beatmapPath);
            
            if (existingMap != null && existingMap.FileHash == fileHash)
            {
                return true; // Already indexed and up to date
            }

            // Parse the .osu file
            var osuFile = _fileParser.Parse(beatmapPath);
            
            // Only index supported mania key counts for MSD analysis (4K/6K/7K for MinaCalc 5.15+, 4K only for 5.05)
            var isSupportedMania = osuFile.Mode == 3 && ToolPaths.IsKeyCountSupported(osuFile.CircleSize);

            var map = new IndexedMap
            {
                BeatmapPath = beatmapPath,
                FileHash = fileHash,
                Title = osuFile.Title,
                Artist = osuFile.Artist,
                Version = osuFile.Version,
                Creator = osuFile.Creator,
                CircleSize = osuFile.CircleSize,
                Mode = osuFile.Mode,
                LastAnalyzed = DateTime.UtcNow
            };

            // Analyze MSD for supported mania key counts
            if (isSupportedMania && _msdAnalyzer != null)
            {
                try
                {
                    var msdResult = await _msdAnalyzer.AnalyzeAsync(beatmapPath, new MsdAnalysisOptions { TimeoutMs = 30000 });
                    
                    map.DominantSkillset = msdResult.DominantSkillset;
                    map.OverallMsd = msdResult.Difficulty1x;

                    // Store scores for all rates
                    foreach (var rate in msdResult.Rates)
                    {
                        map.MsdScores.Add(new MapMsdScore
                        {
                            Rate = rate.Rate,
                            Overall = rate.Scores.Overall,
                            Stream = rate.Scores.Stream,
                            Jumpstream = rate.Scores.Jumpstream,
                            Handstream = rate.Scores.Handstream,
                            Stamina = rate.Scores.Stamina,
                            Jackspeed = rate.Scores.Jackspeed,
                            Chordjack = rate.Scores.Chordjack,
                            Technical = rate.Scores.Technical
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MapsDB] MSD analysis failed for {Path.GetFileName(beatmapPath)}: {ex.Message}");
                    // Store map without MSD data
                }
            }

            // Save to database
            SaveMap(map);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error indexing {beatmapPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Saves or updates a map in the database (thread-safe, for single map operations outside of batch indexing).
    /// </summary>
    private void SaveMap(IndexedMap map)
    {
        lock (_batchWriteLock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                SaveMapInternal(map, connection, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// Cached map info for fast unchanged file detection.
    /// </summary>
    private struct CachedMapInfo
    {
        public string FileHash;
        public long FileSize;
        public long LastModified;
    }

    /// <summary>
    /// Gets a map by its path.
    /// </summary>
    public IndexedMap? GetMapByPath(string beatmapPath)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var query = @"
                SELECT BeatmapPath, FileHash, Title, Artist, Version, Creator, CircleSize, Mode, DominantSkillset, OverallMsd, LastAnalyzed, FileSize, LastModified
                FROM Maps WHERE BeatmapPath = @Path";

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@Path", beatmapPath);
            
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            var map = ReadMap(reader);
            
            // Load MSD scores
            map.MsdScores = GetMapMsdScores(beatmapPath, connection);
            
            // Load player stats
            map.PlayerStats = GetMapPlayerStats(beatmapPath, connection);

            return map;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error getting map: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Searches for maps matching the given criteria.
    /// </summary>
    public List<IndexedMap> SearchMaps(MapSearchCriteria criteria)
    {
        var maps = new List<IndexedMap>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var conditions = new List<string>();
            var parameters = new List<SqliteParameter>();

            // Build WHERE clause
            if (criteria.Skillset != null)
            {
                conditions.Add("DominantSkillset = @Skillset");
                parameters.Add(new SqliteParameter("@Skillset", criteria.Skillset));
            }

            if (criteria.MinMsd.HasValue)
            {
                conditions.Add("OverallMsd >= @MinMsd");
                parameters.Add(new SqliteParameter("@MinMsd", criteria.MinMsd.Value));
            }

            if (criteria.MaxMsd.HasValue)
            {
                conditions.Add("OverallMsd <= @MaxMsd");
                parameters.Add(new SqliteParameter("@MaxMsd", criteria.MaxMsd.Value));
            }

            if (criteria.KeyCount.HasValue)
            {
                conditions.Add("Mode = 3 AND CircleSize = @KeyCount");
                parameters.Add(new SqliteParameter("@KeyCount", criteria.KeyCount.Value));
            }

            if (!string.IsNullOrWhiteSpace(criteria.SearchText))
            {
                conditions.Add("(Title LIKE @Search OR Artist LIKE @Search OR Creator LIKE @Search)");
                parameters.Add(new SqliteParameter("@Search", $"%{criteria.SearchText}%"));
            }

            // Build query
            var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            
            var orderBy = criteria.OrderBy switch
            {
                MapSearchOrderBy.Title => "Title",
                MapSearchOrderBy.Artist => "Artist",
                MapSearchOrderBy.LastAnalyzed => "LastAnalyzed",
                MapSearchOrderBy.PlayCount => "(SELECT COUNT(*) FROM MapPlayerStats WHERE MapPlayerStats.BeatmapPath = Maps.BeatmapPath)",
                MapSearchOrderBy.BestAccuracy => "(SELECT MAX(Accuracy) FROM MapPlayerStats WHERE MapPlayerStats.BeatmapPath = Maps.BeatmapPath)",
                MapSearchOrderBy.Random => "RANDOM()",
                _ => "OverallMsd"
            };

            var direction = criteria.Ascending ? "ASC" : "DESC";
            var limit = criteria.Limit.HasValue ? $"LIMIT {criteria.Limit.Value}" : "";
            var offset = criteria.Offset.HasValue ? $"OFFSET {criteria.Offset.Value}" : "";

            var query = $@"
                SELECT BeatmapPath, FileHash, Title, Artist, Version, Creator, CircleSize, Mode, DominantSkillset, OverallMsd, LastAnalyzed, FileSize, LastModified
                FROM Maps
                {whereClause}
                ORDER BY {orderBy} {direction}
                {limit} {offset}";

            using var cmd = new SqliteCommand(query, connection);
            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var map = ReadMap(reader);
                
                // Filter by played/unplayed if needed
                if (criteria.OnlyPlayed || criteria.OnlyUnplayed || criteria.MinPlayerAccuracy.HasValue || criteria.MaxPlayerAccuracy.HasValue)
                {
                    map.PlayerStats = GetMapPlayerStats(map.BeatmapPath, connection);
                    
                    if (criteria.OnlyPlayed && map.PlayCount == 0) continue;
                    if (criteria.OnlyUnplayed && map.PlayCount > 0) continue;
                    if (criteria.MinPlayerAccuracy.HasValue && (map.BestPlayerAccuracy ?? 0) < criteria.MinPlayerAccuracy.Value) continue;
                    if (criteria.MaxPlayerAccuracy.HasValue && (map.BestPlayerAccuracy ?? 100) > criteria.MaxPlayerAccuracy.Value) continue;
                }

                maps.Add(map);
            }

            // Load MSD scores for returned maps
            foreach (var map in maps)
            {
                map.MsdScores = GetMapMsdScores(map.BeatmapPath, connection);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error searching maps: {ex.Message}");
        }

        return maps;
    }

    /// <summary>
    /// Updates player statistics for a map.
    /// </summary>
    public void UpdatePlayerStats(string beatmapPath, SessionPlayResult play)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var insert = @"
                INSERT INTO MapPlayerStats (BeatmapPath, SessionPlayId, Accuracy, RecordedAt)
                VALUES (@Path, @PlayId, @Accuracy, @RecordedAt)";

            using var cmd = new SqliteCommand(insert, connection);
            cmd.Parameters.AddWithValue("@Path", beatmapPath);
            cmd.Parameters.AddWithValue("@PlayId", DBNull.Value); // Session play ID not tracked yet
            cmd.Parameters.AddWithValue("@Accuracy", play.Accuracy);
            cmd.Parameters.AddWithValue("@RecordedAt", play.RecordedAt.ToString("o"));
            cmd.ExecuteNonQuery();

            Console.WriteLine($"[MapsDB] Updated player stats for: {Path.GetFileName(beatmapPath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error updating player stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Scans the osu! Songs folder and indexes all beatmaps using parallel processing.
    /// </summary>
    public async Task ScanOsuSongsFolderAsync(string songsPath, CancellationToken cancellationToken = default)
    {
        if (_isIndexing)
        {
            Console.WriteLine("[MapsDB] Indexing already in progress");
            return;
        }

        if (!Directory.Exists(songsPath))
        {
            Console.WriteLine($"[MapsDB] Songs folder not found: {songsPath}");
            return;
        }

        _isIndexing = true;
        _indexingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            Console.WriteLine($"[MapsDB] Starting optimized parallel scan of: {songsPath}");

            // Pre-load existing map info into cache for fast lookups
            LoadMapInfoCache();

            // Find all .osu files
            var osuFiles = Directory.GetFiles(songsPath, "*.osu", SearchOption.AllDirectories);
            var total = osuFiles.Length;
            var processed = 0;
            var indexed = 0;
            var skipped = 0;
            var failed = 0;
            var progressLock = new object();

            // Determine parallelism based on CPU cores (leave some headroom)
            var maxParallelism = Math.Max(1, Environment.ProcessorCount - 1);
            Console.WriteLine($"[MapsDB] Using {maxParallelism} parallel workers, {_mapInfoCache?.Count ?? 0} cached entries");

            IndexingProgressChanged?.Invoke(this, new IndexingProgressEventArgs
            {
                TotalFiles = total,
                ProcessedFiles = 0,
                IndexedFiles = 0,
                FailedFiles = 0,
                CurrentFile = "",
                Status = $"Found {total} .osu files (using {maxParallelism} threads)"
            });

            // Use Parallel.ForEachAsync for concurrent processing
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = _indexingCts.Token
            };

            // Start background flush task
            var flushTask = Task.Run(async () =>
            {
                try
                {
                    while (!_indexingCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(500, _indexingCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        if (_pendingWrites.Count >= BatchWriteSize)
                        {
                            FlushPendingWrites();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation requested
                }
            }, CancellationToken.None); // Don't pass token here, handle inside

            await Parallel.ForEachAsync(osuFiles, parallelOptions, async (file, token) =>
            {
                if (token.IsCancellationRequested)
                    return;

                var result = IndexResult.Failed;
                try
                {
                    result = await IndexBeatmapOptimizedAsync(file, token);
                }
                catch
                {
                    // Ignore individual failures
                }

                // Thread-safe progress update
                lock (progressLock)
                {
                    processed++;
                    switch (result)
                    {
                        case IndexResult.Indexed:
                            indexed++;
                            break;
                        case IndexResult.Skipped:
                            skipped++;
                            break;
                        case IndexResult.Failed:
                            failed++;
                            break;
                    }

                    // Report progress every 100 files or at the end (reduced frequency for speed)
                    if (processed % 100 == 0 || processed == total)
                    {
                        IndexingProgressChanged?.Invoke(this, new IndexingProgressEventArgs
                        {
                            TotalFiles = total,
                            ProcessedFiles = processed,
                            IndexedFiles = indexed,
                            FailedFiles = failed,
                            CurrentFile = Path.GetFileName(file),
                            Status = $"Indexing: {processed}/{total} ({(processed * 100) / total}%) - {skipped} unchanged"
                        });
                    }
                }
            });

            // Final flush
            FlushPendingWrites();

            IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs
            {
                TotalFiles = total,
                IndexedFiles = indexed,
                FailedFiles = failed,
                WasCancelled = _indexingCts.Token.IsCancellationRequested
            });

            Console.WriteLine($"[MapsDB] Parallel scan complete: {indexed} indexed, {skipped} unchanged, {failed} failed");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[MapsDB] Indexing was cancelled");
            IndexingCompleted?.Invoke(this, new IndexingCompletedEventArgs
            {
                WasCancelled = true
            });
        }
        finally
        {
            _isIndexing = false;
            _indexingCts?.Dispose();
            _indexingCts = null;
            _mapInfoCache = null; // Clear cache after indexing
        }
    }

    private enum IndexResult
    {
        Indexed,
        Skipped,
        Failed
    }

    /// <summary>
    /// Optimized indexing that uses cache and batch writes.
    /// </summary>
    private async Task<IndexResult> IndexBeatmapOptimizedAsync(string beatmapPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(beatmapPath))
            return IndexResult.Failed;

        try
        {
            // Quick check using cache - avoids reading file if unchanged
            if (!NeedsReindexing(beatmapPath, out var fileHash))
            {
                return IndexResult.Skipped;
            }

            // Get file metadata for caching
            var fileInfo = new FileInfo(beatmapPath);

            // Parse the .osu file
            var osuFile = _fileParser.Parse(beatmapPath);

            // Only index supported mania key counts for MSD analysis (4K/6K/7K for MinaCalc 5.15+, 4K only for 5.05)
            var isSupportedMania = osuFile.Mode == 3 && ToolPaths.IsKeyCountSupported(osuFile.CircleSize);

            var map = new IndexedMap
            {
                BeatmapPath = beatmapPath,
                FileHash = fileHash,
                FileSize = fileInfo.Length,
                LastModifiedTicks = fileInfo.LastWriteTimeUtc.Ticks,
                Title = osuFile.Title,
                Artist = osuFile.Artist,
                Version = osuFile.Version,
                Creator = osuFile.Creator,
                CircleSize = osuFile.CircleSize,
                Mode = osuFile.Mode,
                LastAnalyzed = DateTime.UtcNow
            };

            // Analyze MSD for supported mania key counts
            if (isSupportedMania && _msdAnalyzer != null)
            {
                try
                {
                    var msdResult = await _msdAnalyzer.AnalyzeAsync(beatmapPath, new MsdAnalysisOptions { TimeoutMs = 30000 });

                    map.DominantSkillset = msdResult.DominantSkillset;
                    map.OverallMsd = msdResult.Difficulty1x;

                    // Store scores for all rates
                    foreach (var rate in msdResult.Rates)
                    {
                        map.MsdScores.Add(new MapMsdScore
                        {
                            Rate = rate.Rate,
                            Overall = rate.Scores.Overall,
                            Stream = rate.Scores.Stream,
                            Jumpstream = rate.Scores.Jumpstream,
                            Handstream = rate.Scores.Handstream,
                            Stamina = rate.Scores.Stamina,
                            Jackspeed = rate.Scores.Jackspeed,
                            Chordjack = rate.Scores.Chordjack,
                            Technical = rate.Scores.Technical
                        });
                    }

                    // Fire and forget server upload if API is configured
                    // Skip session copies (they have the companellasessiondonottouch tag)
                    if (_beatmapApiService != null && _beatmapApiService.IsEnabled)
                    {
                        var isSessionCopy = osuFile.Tags.Contains(BeatmapIndexer.SessionTag, StringComparison.OrdinalIgnoreCase);
                        if (!isSessionCopy)
                        {
                            _ = _beatmapApiService.UploadBeatmapAsync(beatmapPath, cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MapsDB] MSD analysis failed for {Path.GetFileName(beatmapPath)}: {ex.Message}");
                    // Store map without MSD data
                }
            }

            // Queue for batch write instead of immediate write
            _pendingWrites.Enqueue(map);

            // Trigger batch flush if queue is large enough
            if (_pendingWrites.Count >= BatchWriteSize)
            {
                FlushPendingWrites();
            }

            return IndexResult.Indexed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error indexing {beatmapPath}: {ex.Message}");
            return IndexResult.Failed;
        }
    }

    /// <summary>
    /// Cancels the current indexing operation.
    /// </summary>
    public void CancelIndexing()
    {
        _indexingCts?.Cancel();
    }

    /// <summary>
    /// Computes MD5 hash of a file.
    /// </summary>
    private string ComputeFileHash(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Loads all existing map info into memory cache for fast lookups.
    /// </summary>
    private void LoadMapInfoCache()
    {
        _mapInfoCache = new ConcurrentDictionary<string, CachedMapInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT BeatmapPath, FileHash, FileSize, LastModified FROM Maps", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var path = reader.GetString(0);
                var info = new CachedMapInfo
                {
                    FileHash = reader.GetString(1),
                    FileSize = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    LastModified = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
                };
                _mapInfoCache[path] = info;
            }

            Console.WriteLine($"[MapsDB] Loaded {_mapInfoCache.Count} cached map entries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MapsDB] Error loading map cache: {ex.Message}");
            _mapInfoCache = new ConcurrentDictionary<string, CachedMapInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Checks if a file needs reindexing using cached metadata and file system info.
    /// </summary>
    private bool NeedsReindexing(string filePath, out string fileHash)
    {
        fileHash = "";

        if (_mapInfoCache == null)
            return true;

        // Get file info for quick check
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            return false;

        var fileSize = fileInfo.Length;
        var lastModified = fileInfo.LastWriteTimeUtc.Ticks;

        // Check if we have cached info for this file
        if (_mapInfoCache.TryGetValue(filePath, out var cached))
        {
            // If file size and modification time match, skip without computing hash
            if (cached.FileSize == fileSize && cached.LastModified == lastModified)
            {
                return false; // File unchanged, no need to reindex
            }
        }

        // File is new or changed, compute hash
        fileHash = ComputeFileHash(filePath);

        // Double-check with hash in case only timestamps changed but content is same
        if (_mapInfoCache.TryGetValue(filePath, out cached) && cached.FileHash == fileHash)
        {
            // Content unchanged, just update the cached metadata
            _mapInfoCache[filePath] = new CachedMapInfo
            {
                FileHash = fileHash,
                FileSize = fileSize,
                LastModified = lastModified
            };
            return false;
        }

        return true; // Needs indexing
    }

    /// <summary>
    /// Flushes pending writes to the database in a batch.
    /// </summary>
    private void FlushPendingWrites()
    {
        var maps = new List<IndexedMap>();

        while (_pendingWrites.TryDequeue(out var map))
        {
            maps.Add(map);
        }

        if (maps.Count == 0)
            return;

        lock (_batchWriteLock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();

                foreach (var map in maps)
                {
                    SaveMapInternal(map, connection, transaction);
                }

                transaction.Commit();
                Console.WriteLine($"[MapsDB] Batch wrote {maps.Count} maps");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapsDB] Batch write error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Internal save method that uses an existing connection and transaction.
    /// </summary>
    private void SaveMapInternal(IndexedMap map, SqliteConnection connection, SqliteTransaction transaction)
    {
        // Delete existing MSD scores
        using (var cmd = new SqliteCommand("DELETE FROM MapMsdScores WHERE BeatmapPath = @Path", connection, transaction))
        {
            cmd.Parameters.AddWithValue("@Path", map.BeatmapPath);
            cmd.ExecuteNonQuery();
        }

        // Insert or replace map
        var insertMap = @"
            INSERT OR REPLACE INTO Maps (BeatmapPath, FileHash, FileSize, LastModified, Title, Artist, Version, Creator, CircleSize, Mode, DominantSkillset, OverallMsd, LastAnalyzed)
            VALUES (@Path, @Hash, @FileSize, @LastModified, @Title, @Artist, @Version, @Creator, @CircleSize, @Mode, @Skillset, @Msd, @Analyzed)";

        using (var cmd = new SqliteCommand(insertMap, connection, transaction))
        {
            cmd.Parameters.AddWithValue("@Path", map.BeatmapPath);
            cmd.Parameters.AddWithValue("@Hash", map.FileHash);
            cmd.Parameters.AddWithValue("@FileSize", map.FileSize);
            cmd.Parameters.AddWithValue("@LastModified", map.LastModifiedTicks);
            cmd.Parameters.AddWithValue("@Title", map.Title ?? "");
            cmd.Parameters.AddWithValue("@Artist", map.Artist ?? "");
            cmd.Parameters.AddWithValue("@Version", map.Version ?? "");
            cmd.Parameters.AddWithValue("@Creator", map.Creator ?? "");
            cmd.Parameters.AddWithValue("@CircleSize", map.CircleSize);
            cmd.Parameters.AddWithValue("@Mode", map.Mode);
            cmd.Parameters.AddWithValue("@Skillset", map.DominantSkillset ?? "");
            cmd.Parameters.AddWithValue("@Msd", map.OverallMsd);
            cmd.Parameters.AddWithValue("@Analyzed", map.LastAnalyzed.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // Insert MSD scores
        if (map.MsdScores.Count > 0)
        {
            var insertScore = @"
                INSERT INTO MapMsdScores (BeatmapPath, Rate, Overall, Stream, Jumpstream, Handstream, Stamina, Jackspeed, Chordjack, Technical)
                VALUES (@Path, @Rate, @Overall, @Stream, @Jumpstream, @Handstream, @Stamina, @Jackspeed, @Chordjack, @Technical)";

            foreach (var score in map.MsdScores)
            {
                using var cmd = new SqliteCommand(insertScore, connection, transaction);
                cmd.Parameters.AddWithValue("@Path", map.BeatmapPath);
                cmd.Parameters.AddWithValue("@Rate", score.Rate);
                cmd.Parameters.AddWithValue("@Overall", score.Overall);
                cmd.Parameters.AddWithValue("@Stream", score.Stream);
                cmd.Parameters.AddWithValue("@Jumpstream", score.Jumpstream);
                cmd.Parameters.AddWithValue("@Handstream", score.Handstream);
                cmd.Parameters.AddWithValue("@Stamina", score.Stamina);
                cmd.Parameters.AddWithValue("@Jackspeed", score.Jackspeed);
                cmd.Parameters.AddWithValue("@Chordjack", score.Chordjack);
                cmd.Parameters.AddWithValue("@Technical", score.Technical);
                cmd.ExecuteNonQuery();
            }
        }

        // Update cache
        _mapInfoCache?.TryAdd(map.BeatmapPath, new CachedMapInfo
        {
            FileHash = map.FileHash,
            FileSize = map.FileSize,
            LastModified = map.LastModifiedTicks
        });
    }

    private IndexedMap ReadMap(SqliteDataReader reader)
    {
        return new IndexedMap
        {
            BeatmapPath = reader.GetString(0),
            FileHash = reader.GetString(1),
            Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Artist = reader.IsDBNull(3) ? "" : reader.GetString(3),
            Version = reader.IsDBNull(4) ? "" : reader.GetString(4),
            Creator = reader.IsDBNull(5) ? "" : reader.GetString(5),
            CircleSize = reader.IsDBNull(6) ? 4 : reader.GetDouble(6),
            Mode = reader.IsDBNull(7) ? 3 : reader.GetInt32(7),
            DominantSkillset = reader.IsDBNull(8) ? "" : reader.GetString(8),
            OverallMsd = reader.IsDBNull(9) ? 0 : (float)reader.GetDouble(9),
            LastAnalyzed = DateTime.Parse(reader.GetString(10)),
            FileSize = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetInt64(11) : 0,
            LastModifiedTicks = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetInt64(12) : 0
        };
    }

    private List<MapMsdScore> GetMapMsdScores(string beatmapPath, SqliteConnection connection)
    {
        var scores = new List<MapMsdScore>();

        var query = @"
            SELECT Rate, Overall, Stream, Jumpstream, Handstream, Stamina, Jackspeed, Chordjack, Technical
            FROM MapMsdScores WHERE BeatmapPath = @Path";

        using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@Path", beatmapPath);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            scores.Add(new MapMsdScore
            {
                Rate = (float)reader.GetDouble(0),
                Overall = (float)reader.GetDouble(1),
                Stream = (float)reader.GetDouble(2),
                Jumpstream = (float)reader.GetDouble(3),
                Handstream = (float)reader.GetDouble(4),
                Stamina = (float)reader.GetDouble(5),
                Jackspeed = (float)reader.GetDouble(6),
                Chordjack = (float)reader.GetDouble(7),
                Technical = (float)reader.GetDouble(8)
            });
        }

        return scores;
    }

    private List<MapPlayerStat> GetMapPlayerStats(string beatmapPath, SqliteConnection connection)
    {
        var stats = new List<MapPlayerStat>();

        var query = @"
            SELECT Id, Accuracy, RecordedAt
            FROM MapPlayerStats WHERE BeatmapPath = @Path
            ORDER BY RecordedAt DESC";

        using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@Path", beatmapPath);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            stats.Add(new MapPlayerStat
            {
                SessionPlayId = reader.GetInt64(0),
                Accuracy = reader.GetDouble(1),
                RecordedAt = DateTime.Parse(reader.GetString(2))
            });
        }

        return stats;
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        CancelIndexing();
        _isDisposed = true;
    }
}

/// <summary>
/// Event args for indexing progress updates.
/// </summary>
public class IndexingProgressEventArgs : EventArgs
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int IndexedFiles { get; set; }
    public int FailedFiles { get; set; }
    public string CurrentFile { get; set; } = "";
    public string Status { get; set; } = "";
    public int ProgressPercentage => TotalFiles > 0 ? (ProcessedFiles * 100) / TotalFiles : 0;
}

/// <summary>
/// Event args for indexing completion.
/// </summary>
public class IndexingCompletedEventArgs : EventArgs
{
    public int TotalFiles { get; set; }
    public int IndexedFiles { get; set; }
    public int FailedFiles { get; set; }
    public bool WasCancelled { get; set; }
}
