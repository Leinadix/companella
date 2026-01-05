using Microsoft.Data.Sqlite;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service for persisting session data to a SQLite database.
/// Database is stored in %AppData%\Companella to preserve data across updates.
/// </summary>
public class SessionDatabaseService : IDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private bool _isDisposed;

    /// <summary>
    /// Creates a new SessionDatabaseService.
    /// </summary>
    public SessionDatabaseService()
    {
        // Use DataPaths for AppData-based storage
        _databasePath = DataPaths.SessionsDatabase;
        _connectionString = $"Data Source={_databasePath}";
        
        InitializeDatabase();
    }

    /// <summary>
    /// Initializes the database and creates tables if they don't exist.
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var createSessionsTable = @"
                CREATE TABLE IF NOT EXISTS Sessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    TotalPlays INTEGER NOT NULL,
                    AverageAccuracy REAL NOT NULL,
                    BestAccuracy REAL NOT NULL,
                    WorstAccuracy REAL NOT NULL,
                    AverageMsd REAL NOT NULL,
                    TotalTimePlayed TEXT NOT NULL
                )";

            var createPlaysTable = @"
                CREATE TABLE IF NOT EXISTS SessionPlays (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    BeatmapPath TEXT NOT NULL,
                    Accuracy REAL NOT NULL,
                    SessionTime TEXT NOT NULL,
                    RecordedAt TEXT NOT NULL,
                    HighestMsdValue REAL NOT NULL,
                    DominantSkillset TEXT NOT NULL,
                    FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
                )";

            using (var cmd = new SqliteCommand(createSessionsTable, connection))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand(createPlaysTable, connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Create index for faster lookups
            var createIndex = @"
                CREATE INDEX IF NOT EXISTS idx_sessionplays_sessionid 
                ON SessionPlays(SessionId)";

            using (var cmd = new SqliteCommand(createIndex, connection))
            {
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine($"[SessionDB] Database initialized at: {_databasePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error initializing database: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Saves a session and its plays to the database.
    /// </summary>
    /// <param name="startTime">Session start time (UTC)</param>
    /// <param name="endTime">Session end time (UTC)</param>
    /// <param name="plays">List of plays in the session</param>
    /// <returns>The ID of the saved session</returns>
    public long SaveSession(DateTime startTime, DateTime endTime, List<SessionPlayResult> plays)
    {
        if (plays.Count == 0)
        {
            Console.WriteLine("[SessionDB] No plays to save, skipping session");
            return -1;
        }

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();

            try
            {
                // Calculate session statistics
                var totalPlays = plays.Count;
                var avgAccuracy = plays.Average(p => p.Accuracy);
                var bestAccuracy = plays.Max(p => p.Accuracy);
                var worstAccuracy = plays.Min(p => p.Accuracy);
                var avgMsd = plays.Average(p => p.HighestMsdValue);
                var totalTime = plays.Max(p => p.SessionTime);

                // Insert session
                var insertSession = @"
                    INSERT INTO Sessions (StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed)
                    VALUES (@StartTime, @EndTime, @TotalPlays, @AverageAccuracy, @BestAccuracy, @WorstAccuracy, @AverageMsd, @TotalTimePlayed);
                    SELECT last_insert_rowid();";

                long sessionId;
                using (var cmd = new SqliteCommand(insertSession, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@StartTime", startTime.ToString("o"));
                    cmd.Parameters.AddWithValue("@EndTime", endTime.ToString("o"));
                    cmd.Parameters.AddWithValue("@TotalPlays", totalPlays);
                    cmd.Parameters.AddWithValue("@AverageAccuracy", avgAccuracy);
                    cmd.Parameters.AddWithValue("@BestAccuracy", bestAccuracy);
                    cmd.Parameters.AddWithValue("@WorstAccuracy", worstAccuracy);
                    cmd.Parameters.AddWithValue("@AverageMsd", avgMsd);
                    cmd.Parameters.AddWithValue("@TotalTimePlayed", totalTime.ToString());

                    sessionId = (long)cmd.ExecuteScalar()!;
                }

                // Insert plays
                var insertPlay = @"
                    INSERT INTO SessionPlays (SessionId, BeatmapPath, Accuracy, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset)
                    VALUES (@SessionId, @BeatmapPath, @Accuracy, @SessionTime, @RecordedAt, @HighestMsdValue, @DominantSkillset)";

                foreach (var play in plays)
                {
                    using var cmd = new SqliteCommand(insertPlay, connection, transaction);
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    cmd.Parameters.AddWithValue("@BeatmapPath", play.BeatmapPath);
                    cmd.Parameters.AddWithValue("@Accuracy", play.Accuracy);
                    cmd.Parameters.AddWithValue("@SessionTime", play.SessionTime.ToString());
                    cmd.Parameters.AddWithValue("@RecordedAt", play.RecordedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("@HighestMsdValue", play.HighestMsdValue);
                    cmd.Parameters.AddWithValue("@DominantSkillset", play.DominantSkillset);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                Console.WriteLine($"[SessionDB] Saved session {sessionId} with {totalPlays} plays");
                return sessionId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error saving session: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets all sessions (without plays) ordered by start time descending.
    /// </summary>
    public List<StoredSession> GetSessions()
    {
        var sessions = new List<StoredSession>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var query = @"
                SELECT Id, StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed
                FROM Sessions
                ORDER BY StartTime DESC";

            using var cmd = new SqliteCommand(query, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                sessions.Add(ReadSession(reader));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting sessions: {ex.Message}");
        }

        return sessions;
    }

    /// <summary>
    /// Gets a session by ID with all its plays.
    /// </summary>
    public StoredSession? GetSessionById(long sessionId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Get session
            var sessionQuery = @"
                SELECT Id, StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed
                FROM Sessions
                WHERE Id = @Id";

            StoredSession? session = null;
            using (var cmd = new SqliteCommand(sessionQuery, connection))
            {
                cmd.Parameters.AddWithValue("@Id", sessionId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    session = ReadSession(reader);
                }
            }

            if (session == null)
                return null;

            // Get plays
            session.Plays = GetSessionPlays(sessionId, connection);

            return session;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting session {sessionId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the plays for a session.
    /// </summary>
    public List<StoredSessionPlay> GetSessionPlays(long sessionId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return GetSessionPlays(sessionId, connection);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting plays for session {sessionId}: {ex.Message}");
            return new List<StoredSessionPlay>();
        }
    }

    private List<StoredSessionPlay> GetSessionPlays(long sessionId, SqliteConnection connection)
    {
        var plays = new List<StoredSessionPlay>();

        var query = @"
            SELECT Id, SessionId, BeatmapPath, Accuracy, SessionTime, RecordedAt, HighestMsdValue, DominantSkillset
            FROM SessionPlays
            WHERE SessionId = @SessionId
            ORDER BY SessionTime ASC";

        using var cmd = new SqliteCommand(query, connection);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            plays.Add(ReadPlay(reader));
        }

        return plays;
    }

    /// <summary>
    /// Deletes a session and all its plays.
    /// </summary>
    public bool DeleteSession(long sessionId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Delete plays first (cascade should handle this, but be explicit)
            using (var cmd = new SqliteCommand("DELETE FROM SessionPlays WHERE SessionId = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", sessionId);
                cmd.ExecuteNonQuery();
            }

            // Delete session
            using (var cmd = new SqliteCommand("DELETE FROM Sessions WHERE Id = @Id", connection))
            {
                cmd.Parameters.AddWithValue("@Id", sessionId);
                var affected = cmd.ExecuteNonQuery();
                Console.WriteLine($"[SessionDB] Deleted session {sessionId}");
                return affected > 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error deleting session {sessionId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the total number of sessions.
    /// </summary>
    public int GetSessionCount()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Sessions", connection);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting session count: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Gets sessions within a date range.
    /// </summary>
    public List<StoredSession> GetSessionsInRange(DateTime startDate, DateTime endDate)
    {
        var sessions = new List<StoredSession>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var query = @"
                SELECT Id, StartTime, EndTime, TotalPlays, AverageAccuracy, BestAccuracy, WorstAccuracy, AverageMsd, TotalTimePlayed
                FROM Sessions
                WHERE StartTime >= @StartDate AND StartTime <= @EndDate
                ORDER BY StartTime DESC";

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("o"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("o"));
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                sessions.Add(ReadSession(reader));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting sessions in range: {ex.Message}");
        }

        return sessions;
    }

    /// <summary>
    /// Gets all plays within a date range across all sessions.
    /// Used for trend analysis.
    /// </summary>
    public List<StoredSessionPlay> GetPlaysInTimeRange(DateTime startDate, DateTime endDate)
    {
        var plays = new List<StoredSessionPlay>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var query = @"
                SELECT sp.Id, sp.SessionId, sp.BeatmapPath, sp.Accuracy, sp.SessionTime, sp.RecordedAt, sp.HighestMsdValue, sp.DominantSkillset
                FROM SessionPlays sp
                INNER JOIN Sessions s ON sp.SessionId = s.Id
                WHERE sp.RecordedAt >= @StartDate AND sp.RecordedAt <= @EndDate
                ORDER BY sp.RecordedAt ASC";

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("o"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("o"));
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                plays.Add(ReadPlay(reader));
            }

            Console.WriteLine($"[SessionDB] Found {plays.Count} plays in time range");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting plays in time range: {ex.Message}");
        }

        return plays;
    }

    /// <summary>
    /// Gets total play count across all sessions.
    /// </summary>
    public int GetTotalPlayCount()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM SessionPlays", connection);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionDB] Error getting total play count: {ex.Message}");
            return 0;
        }
    }

    private StoredSession ReadSession(SqliteDataReader reader)
    {
        return new StoredSession
        {
            Id = reader.GetInt64(0),
            StartTime = DateTime.Parse(reader.GetString(1)),
            EndTime = DateTime.Parse(reader.GetString(2)),
            TotalPlays = reader.GetInt32(3),
            AverageAccuracy = reader.GetDouble(4),
            BestAccuracy = reader.GetDouble(5),
            WorstAccuracy = reader.GetDouble(6),
            AverageMsd = reader.GetDouble(7),
            TotalTimePlayed = TimeSpan.Parse(reader.GetString(8))
        };
    }

    private StoredSessionPlay ReadPlay(SqliteDataReader reader)
    {
        return new StoredSessionPlay
        {
            Id = reader.GetInt64(0),
            SessionId = reader.GetInt64(1),
            BeatmapPath = reader.GetString(2),
            Accuracy = reader.GetDouble(3),
            SessionTime = TimeSpan.Parse(reader.GetString(4)),
            RecordedAt = DateTime.Parse(reader.GetString(5)),
            HighestMsdValue = (float)reader.GetDouble(6),
            DominantSkillset = reader.GetString(7)
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
    }
}

