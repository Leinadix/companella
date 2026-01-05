using OsuMemoryDataProvider;
using OsuMemoryDataProvider.OsuMemoryModels;
using OsuMemoryDataProvider.OsuMemoryModels.Direct;
using OsuMappingHelper.Models;

namespace OsuMappingHelper.Services;

/// <summary>
/// Service that tracks player sessions by monitoring osu! gameplay.
/// Detects completed maps and records accuracy and MSD ratings.
/// </summary>
public class SessionTrackerService : IDisposable
{
    private readonly OsuProcessDetector _processDetector;
    private readonly SessionDatabaseService _databaseService;
    private readonly StructuredOsuMemoryReader _memoryReader;
    private readonly AptabaseService? _aptabaseService;
    private readonly object _lockObj = new();
    
    private Thread? _trackingThread;
    private CancellationTokenSource? _cancellation;
    private bool _isDisposed;
    
    // Session data
    private DateTime _sessionStartTime;
    private readonly List<SessionPlayResult> _plays = new();
    
    // State tracking
    private int _previousStatus = -1;
    private string? _currentPlayingBeatmap;
    private bool _wasPlaying;
    private double _lastAccuracy;
    private float _currentPlayRate = 1.0f;
    
    // Configuration
    private const int PollIntervalMs = 150;
    
    /// <summary>
    /// Whether the session tracker is currently active.
    /// </summary>
    public bool IsTracking { get; private set; }
    
    /// <summary>
    /// The time when the current session started.
    /// </summary>
    public DateTime SessionStartTime => _sessionStartTime;
    
    /// <summary>
    /// Gets the duration of the current session.
    /// </summary>
    public TimeSpan SessionDuration => IsTracking ? DateTime.UtcNow - _sessionStartTime : TimeSpan.Zero;
    
    /// <summary>
    /// Gets a copy of all recorded plays in this session.
    /// </summary>
    public List<SessionPlayResult> Plays
    {
        get
        {
            lock (_lockObj)
            {
                return new List<SessionPlayResult>(_plays);
            }
        }
    }
    
    /// <summary>
    /// Gets the number of plays recorded in this session.
    /// </summary>
    public int PlayCount
    {
        get
        {
            lock (_lockObj)
            {
                return _plays.Count;
            }
        }
    }
    
    /// <summary>
    /// Event raised when a new play is recorded.
    /// </summary>
    public event EventHandler<SessionPlayResult>? PlayRecorded;
    
    /// <summary>
    /// Event raised when the session starts.
    /// </summary>
    public event EventHandler? SessionStarted;
    
    /// <summary>
    /// Event raised when the session stops.
    /// </summary>
    public event EventHandler? SessionStopped;
    
    /// <summary>
    /// Event raised when there's a status update (for UI feedback).
    /// </summary>
    public event EventHandler<string>? StatusUpdated;
    
    /// <summary>
    /// Creates a new SessionTrackerService.
    /// </summary>
    public SessionTrackerService(OsuProcessDetector processDetector, SessionDatabaseService databaseService, AptabaseService? aptabaseService = null)
    {
        _processDetector = processDetector;
        _databaseService = databaseService;
        _memoryReader = StructuredOsuMemoryReader.Instance;
        _aptabaseService = aptabaseService;
    }
    
    /// <summary>
    /// Starts tracking the session.
    /// </summary>
    public void StartSession()
    {
        if (IsTracking)
            return;
        
        lock (_lockObj)
        {
            _plays.Clear();
        }
        
        _sessionStartTime = DateTime.UtcNow;
        _previousStatus = -1;
        _currentPlayingBeatmap = null;
        _wasPlaying = false;
        _lastAccuracy = 0;
        
        _cancellation = new CancellationTokenSource();
        _trackingThread = new Thread(TrackingLoop)
        {
            Name = "SessionTrackerThread",
            IsBackground = true
        };
        
        IsTracking = true;
        _trackingThread.Start();
        
        // Track analytics
        _aptabaseService?.TrackSessionStart();
        
        SessionStarted?.Invoke(this, EventArgs.Empty);
        StatusUpdated?.Invoke(this, "Session started");
        Console.WriteLine("[Session] Session tracking started");
    }
    
    /// <summary>
    /// Stops tracking the session.
    /// </summary>
    public void StopSession()
    {
        if (!IsTracking)
            return;
        
        _cancellation?.Cancel();
        
        // Wait for thread to finish
        if (_trackingThread != null && _trackingThread.IsAlive)
        {
            _trackingThread.Join(1000);
        }
        
        _cancellation?.Dispose();
        _cancellation = null;
        _trackingThread = null;
        
        IsTracking = false;
        
        // Track analytics
        var durationMinutes = (DateTime.UtcNow - _sessionStartTime).TotalMinutes;
        int playCount;
        lock (_lockObj)
        {
            playCount = _plays.Count;
        }
        _aptabaseService?.TrackSessionStop(durationMinutes, playCount);
        
        // Save session to database
        SaveSessionToDatabase();
        
        SessionStopped?.Invoke(this, EventArgs.Empty);
        StatusUpdated?.Invoke(this, "Session stopped");
        Console.WriteLine("[Session] Session tracking stopped");
    }
    
    /// <summary>
    /// Saves the current session to the database.
    /// </summary>
    private void SaveSessionToDatabase()
    {
        List<SessionPlayResult> playsToSave;
        lock (_lockObj)
        {
            if (_plays.Count == 0)
            {
                Console.WriteLine("[Session] No plays to save");
                return;
            }
            playsToSave = new List<SessionPlayResult>(_plays);
        }
        
        try
        {
            var endTime = DateTime.UtcNow;
            var sessionId = _databaseService.SaveSession(_sessionStartTime, endTime, playsToSave);
            
            if (sessionId > 0)
            {
                Console.WriteLine($"[Session] Session saved to database with ID: {sessionId}");
                StatusUpdated?.Invoke(this, $"Session saved ({playsToSave.Count} plays)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] Error saving session to database: {ex.Message}");
            StatusUpdated?.Invoke(this, "Failed to save session");
        }
    }
    
    /// <summary>
    /// The main tracking loop that runs on a background thread.
    /// </summary>
    private void TrackingLoop()
    {
        var token = _cancellation?.Token ?? CancellationToken.None;
        
        while (!token.IsCancellationRequested)
        {
            try
            {
                PollGameState();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session] Error in tracking loop: {ex.Message}");
            }
            
            try
            {
                Thread.Sleep(PollIntervalMs);
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
        }
    }
    
    /// <summary>
    /// Polls the current game state and detects map completion.
    /// </summary>
    private void PollGameState()
    {
        if (!_processDetector.IsOsuRunning)
        {
            return;
        }
        
        if (!_memoryReader.CanRead)
        {
            return;
        }
        
        try
        {
            // Read general data which contains game status
            var generalData = new GeneralData();
            if (!_memoryReader.TryRead(generalData))
            {
                return;
            }
            
            int currentStatus = generalData.RawStatus;
            
            // OsuMemoryStatus values from OsuMemoryDataProvider:
            // 0 = MainMenu, 1 = EditingMap, 2 = Playing, 3 = GameShutdownAnimation
            // 4 = SongSelectEdit, 5 = SongSelect, 6 = WIP_NoIdeaWhatThisIs
            // 7 = ResultsScreen, 11 = MultiplayerRooms, 12 = MultiplayerRoom
            // 13 = MultiplayerSongSelect, 14 = MultiplayerResultsScreen
            // 15 = OsuDirect, 19 = RankingTagCoop, 20 = RankingTeam
            // 22 = ProcessingBeatmaps, 23 = Tourney
            
            const int STATUS_PLAYING = 2;
            const int STATUS_RESULTS = 7;
            const int STATUS_SONGSELECT = 5;
            
            // Log status changes for debugging
            if (currentStatus != _previousStatus)
            {
                Console.WriteLine($"[Session] Status changed: {_previousStatus} -> {currentStatus}");
            }
            
            // Also read player data to track accuracy during gameplay
            var player = new Player();
            if (_memoryReader.TryRead(player))
            {
                if (_wasPlaying && player.Accuracy > 0)
                {
                    _lastAccuracy = player.Accuracy;
                }
            }
            
            // Detect transition from Playing to Results or SongSelect
            if (_wasPlaying && currentStatus != STATUS_PLAYING)
            {
                Console.WriteLine($"[Session] Detected end of play, status: {currentStatus}, last accuracy: {_lastAccuracy:F2}%");
                
                // Player just finished playing (or quit)
                if (currentStatus == STATUS_RESULTS)
                {
                    // Successfully completed the map - record the play
                    OnMapCompleted();
                }
                else if (currentStatus == STATUS_SONGSELECT)
                {
                    // Player quit/failed - don't record
                    Console.WriteLine("[Session] Play quit/failed - not recording");
                }
                
                _wasPlaying = false;
                _currentPlayingBeatmap = null;
                _lastAccuracy = 0;
                _currentPlayRate = 1.0f;
            }
            else if (currentStatus == STATUS_PLAYING && !_wasPlaying)
            {
                // Just started playing - capture beatmap path and rate
                _wasPlaying = true;
                _currentPlayingBeatmap = _processDetector.GetBeatmapFromMemory();
                _currentPlayRate = _processDetector.GetCurrentRateFromMods();
                _lastAccuracy = 0;
                
                var rateInfo = Math.Abs(_currentPlayRate - 1.0f) > 0.01f ? $" @ {_currentPlayRate:F2}x" : "";
                Console.WriteLine($"[Session] Started playing: {Path.GetFileName(_currentPlayingBeatmap ?? "Unknown")}{rateInfo}");
            }
            
            _previousStatus = currentStatus;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] Error polling game state: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Called when a map is completed. Reads accuracy and triggers MSD analysis.
    /// </summary>
    private void OnMapCompleted()
    {
        try
        {
            // Try to read accuracy from player data, fall back to last recorded accuracy
            double accuracy = _lastAccuracy;
            
            var player = new Player();
            if (_memoryReader.TryRead(player) && player.Accuracy > 0)
            {
                accuracy = player.Accuracy;
                Console.WriteLine($"[Session] Read accuracy from memory: {accuracy:F2}%");
            }
            else
            {
                Console.WriteLine($"[Session] Using last recorded accuracy: {accuracy:F2}%");
            }
            
            // Skip if accuracy is 0 (likely invalid data)
            if (accuracy <= 0)
            {
                Console.WriteLine("[Session] Invalid accuracy (0 or negative), skipping play");
                return;
            }
            
            // Get beatmap path - prefer the one we captured when playing started
            string? beatmapPath = _currentPlayingBeatmap ?? _processDetector.GetBeatmapFromMemory();
            
            if (string.IsNullOrEmpty(beatmapPath))
            {
                Console.WriteLine("[Session] Could not determine beatmap path");
                return;
            }
            
            var rateInfo = Math.Abs(_currentPlayRate - 1.0f) > 0.01f ? $" @ {_currentPlayRate:F2}x" : "";
            Console.WriteLine($"[Session] Map completed: {Path.GetFileName(beatmapPath)} - {accuracy:F2}%{rateInfo}");
            StatusUpdated?.Invoke(this, $"Completed: {Path.GetFileName(beatmapPath)} ({accuracy:F2}%){rateInfo}");
            
            // Analyze MSD with the rate used during play (this happens synchronously on the tracking thread)
            AnalyzeAndRecordPlay(beatmapPath, accuracy, _currentPlayRate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] Error recording completed map: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Analyzes the MSD for the beatmap and records the play.
    /// </summary>
    /// <param name="beatmapPath">Path to the beatmap file.</param>
    /// <param name="accuracy">Accuracy achieved on the play.</param>
    /// <param name="rate">Rate the map was played at (1.5 for DT, 0.75 for HT, 1.0 for normal).</param>
    private void AnalyzeAndRecordPlay(string beatmapPath, double accuracy, float rate = 1.0f)
    {
        float highestMsd = 0;
        string dominantSkillset = "unknown";
        
        try
        {
            if (ToolPaths.MsdCalculatorExists)
            {
                var analyzer = new MsdAnalyzer(ToolPaths.MsdCalculator);
                var result = analyzer.AnalyzeSingleRate(beatmapPath, rate, 30000);
                
                if (result?.Scores != null)
                {
                    var dominant = result.Scores.GetDominantSkillset();
                    highestMsd = dominant.Value;
                    dominantSkillset = dominant.Name;
                    var rateInfo = Math.Abs(rate - 1.0f) > 0.01f ? $" @ {rate:F2}x" : "";
                    Console.WriteLine($"[Session] MSD: {highestMsd:F2} ({dominantSkillset}){rateInfo}");
                }
            }
            else
            {
                Console.WriteLine("[Session] MSD calculator not found - using 0 for MSD");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] MSD analysis failed: {ex.Message}");
        }
        
        // Create the play result
        var sessionTime = DateTime.UtcNow - _sessionStartTime;
        var playResult = new SessionPlayResult(
            beatmapPath,
            accuracy,
            sessionTime,
            DateTime.UtcNow,
            highestMsd,
            dominantSkillset
        );
        
        // Add to list
        lock (_lockObj)
        {
            _plays.Add(playResult);
        }
        
        // Track analytics
        _aptabaseService?.TrackPlayRecorded(accuracy, highestMsd, dominantSkillset);
        
        // Raise event
        PlayRecorded?.Invoke(this, playResult);
        Console.WriteLine($"[Session] Play #{PlayCount} recorded");
    }
    
    /// <summary>
    /// Clears all recorded plays without stopping the session.
    /// </summary>
    public void ClearPlays()
    {
        lock (_lockObj)
        {
            _plays.Clear();
        }
        Console.WriteLine("[Session] Plays cleared");
    }
    
    public void Dispose()
    {
        if (_isDisposed)
            return;
        
        StopSession();
        _isDisposed = true;
    }
}

